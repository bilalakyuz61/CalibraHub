using System.Diagnostics;
using System.Globalization;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Persistence.Services;

/// <summary>
/// 2026-08-15 — Harici SQL Server kaynağından salt-okunur erişim (veritabanı üzerinden
/// içe aktarım). Güvenlik duruşu:
///   • Yalnız SELECT üretilir; INSERT/UPDATE/EXEC yolu YOKTUR (serbest SQL kabul edilmez).
///   • Şema/nesne/kolon adları kullanıcı metninden alınmaz — kaynağın <c>sys</c> kataloğuna
///     parametreli sorguyla doğrulanır, sonra köşeli ayraçla kaçırılarak birleştirilir.
///   • Değerler <c>ImportParse</c>'ın çözebildiği biçimde metne dönüştürülür.
/// Hatalar yutulmaz: loglanır ve çağırana taşınır (CLAUDE.md "sessiz catch" kuralı).
/// </summary>
public sealed class SqlServerExternalDbReader : IExternalDbReader
{
    /// <summary>Tek okumada izin verilen mutlak satır tavanı — kaçak sorgulara karşı emniyet.</summary>
    public const int AbsoluteMaxRows = 200_000;

    private readonly ILogger<SqlServerExternalDbReader> _logger;
    private readonly Database.SqlServerConnectionFactory _hostFactory;

    public SqlServerExternalDbReader(
        ILogger<SqlServerExternalDbReader> logger,
        Database.SqlServerConnectionFactory hostFactory)
    {
        _logger = logger;
        _hostFactory = hostFactory;
    }

    public async Task<ExternalDbTestResultDto> TestAsync(ExternalDbConnection connection, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = await OpenAsync(connection, ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT @@VERSION;";
            cmd.CommandTimeout = connection.ConnectTimeoutSeconds;
            var version = (await cmd.ExecuteScalarAsync(ct))?.ToString();
            sw.Stop();
            return new ExternalDbTestResultDto(true, null, FirstLine(version), (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Harici SQL bağlantı testi başarısız. Bağlantı={Name} Sunucu={Server} DB={Database}",
                connection.Name, connection.ServerName, connection.DatabaseName);
            return new ExternalDbTestResultDto(false, ex.Message, null, (int)sw.ElapsedMilliseconds);
        }
    }

    public async Task<IReadOnlyList<ExternalDbObjectDto>> ListObjectsAsync(
        ExternalDbConnection connection, CancellationToken ct)
    {
        await using var conn = await OpenAsync(connection, ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = connection.CommandTimeoutSeconds;
        cmd.CommandText = """
            SELECT s.[name] AS SchemaName,
                   o.[name] AS ObjectName,
                   CASE o.[type] WHEN 'U' THEN 'Table' ELSE 'View' END AS Kind
            FROM sys.objects o
            JOIN sys.schemas s ON s.[schema_id] = o.[schema_id]
            WHERE o.[type] IN ('U','V')
              AND o.[is_ms_shipped] = 0
            ORDER BY s.[name], o.[name];
            """;

        var list = new List<ExternalDbObjectDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new ExternalDbObjectDto(r.GetString(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    public async Task<IReadOnlyList<ExternalDbColumnDto>> ListColumnsAsync(
        ExternalDbConnection connection, string schemaName, string objectName, CancellationToken ct)
    {
        await using var conn = await OpenAsync(connection, ct);
        return await ListColumnsAsync(conn, connection, schemaName, objectName, ct);
    }

    public async Task<ExternalDbSampleDto> ReadRowsAsync(
        ExternalDbConnection connection,
        string schemaName,
        string objectName,
        IReadOnlyCollection<string>? columns,
        int maxRows,
        CancellationToken ct)
    {
        if (maxRows <= 0) maxRows = 100;
        if (maxRows > AbsoluteMaxRows) maxRows = AbsoluteMaxRows;

        try
        {
            await using var conn = await OpenAsync(connection, ct);

            // Nesne ve kolonlar kaynağın kendi kataloğundan doğrulanır — kullanıcı metni
            // hiçbir zaman doğrudan SQL'e girmez.
            var available = await ListColumnsAsync(conn, connection, schemaName, objectName, ct);
            if (available.Count == 0)
            {
                return new ExternalDbSampleDto(false,
                    $"Kaynakta '{schemaName}.{objectName}' bulunamadı veya okunabilir kolonu yok.",
                    Array.Empty<ExternalDbColumnDto>(),
                    Array.Empty<IReadOnlyDictionary<string, string?>>(), 0);
            }

            var selected = ResolveSelectedColumns(available, columns, out var unknown);
            if (unknown.Count > 0)
            {
                return new ExternalDbSampleDto(false,
                    $"Kaynakta olmayan kolon(lar): {string.Join(", ", unknown)}",
                    available, Array.Empty<IReadOnlyDictionary<string, string?>>(), 0);
            }

            var columnSql = string.Join(", ", selected.Select(c => $"[{Escape(c.Name)}]"));
            var target = $"[{Escape(schemaName)}].[{Escape(objectName)}]";

            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = connection.CommandTimeoutSeconds;
            cmd.CommandText = $"SELECT TOP (@MaxRows) {columnSql} FROM {target};";
            cmd.Parameters.Add(new SqlParameter("@MaxRows", maxRows));

            var rows = new List<IReadOnlyDictionary<string, string?>>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var d = new Dictionary<string, string?>(selected.Count, StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < selected.Count; i++)
                    d[selected[i].Name] = r.IsDBNull(i) ? null : FormatValue(r.GetValue(i));
                rows.Add(d);
            }

            return new ExternalDbSampleDto(true, null, selected, rows, rows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Harici SQL okuma başarısız. Bağlantı={Name} Nesne={Schema}.{Object} MaxRows={MaxRows}",
                connection.Name, schemaName, objectName, maxRows);
            return new ExternalDbSampleDto(false, ex.Message,
                Array.Empty<ExternalDbColumnDto>(),
                Array.Empty<IReadOnlyDictionary<string, string?>>(), 0);
        }
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<ExternalDbColumnDto>> ListColumnsAsync(
        SqlConnection conn, ExternalDbConnection settings, string schemaName, string objectName, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = settings.CommandTimeoutSeconds;
        cmd.CommandText = """
            SELECT c.[name]        AS ColumnName,
                   t.[name]        AS DataType,
                   c.[is_nullable] AS IsNullable,
                   c.[max_length]  AS MaxLength,
                   c.[column_id]   AS Ordinal
            FROM sys.columns c
            JOIN sys.objects o  ON o.[object_id] = c.[object_id]
            JOIN sys.schemas s  ON s.[schema_id] = o.[schema_id]
            JOIN sys.types   t  ON t.[user_type_id] = c.[user_type_id]
            WHERE o.[type] IN ('U','V')
              AND s.[name] = @Schema
              AND o.[name] = @Object
            ORDER BY c.[column_id];
            """;
        cmd.Parameters.Add(new SqlParameter("@Schema", schemaName));
        cmd.Parameters.Add(new SqlParameter("@Object", objectName));

        var list = new List<ExternalDbColumnDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var maxLength = r.IsDBNull(3) ? (int?)null : r.GetInt16(3);
            list.Add(new ExternalDbColumnDto(
                r.GetString(0), r.GetString(1), r.GetBoolean(2),
                maxLength is -1 ? null : maxLength, r.GetInt32(4)));
        }
        return list;
    }

    private static List<ExternalDbColumnDto> ResolveSelectedColumns(
        IReadOnlyList<ExternalDbColumnDto> available,
        IReadOnlyCollection<string>? requested,
        out List<string> unknown)
    {
        unknown = new List<string>();
        if (requested is null || requested.Count == 0) return available.ToList();

        var byName = available.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var selected = new List<ExternalDbColumnDto>(requested.Count);
        foreach (var name in requested.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (byName.TryGetValue(name, out var col)) selected.Add(col);
            else unknown.Add(name);
        }
        return selected;
    }

    /// <summary>
    /// Değeri <c>ImportParse</c>'ın geri çözebileceği biçimde metne dönüştürür:
    /// ondalık invariant nokta ayracıyla, tarih "yyyy-MM-dd[ HH:mm:ss]", bit "1"/"0".
    /// </summary>
    private static string FormatValue(object value) => value switch
    {
        null or DBNull => string.Empty,
        bool b         => b ? "1" : "0",
        DateTime dt    => dt.TimeOfDay == TimeSpan.Zero
                              ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                              : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        TimeSpan ts    => ts.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
        decimal m      => m.ToString(CultureInfo.InvariantCulture),
        double d       => d.ToString("R", CultureInfo.InvariantCulture),
        float f        => f.ToString("R", CultureInfo.InvariantCulture),
        byte[] bytes   => Convert.ToBase64String(bytes),
        IFormattable fm => fm.ToString(null, CultureInfo.InvariantCulture),
        _              => value.ToString() ?? string.Empty,
    };

    private Task<SqlConnection> OpenAsync(ExternalDbConnection settings, CancellationToken ct)
        => ExternalDbConnectionStringBuilder.OpenAsync(
            settings, "CalibraHub.DataImport.Read", ct, ResolveHostConnectionString(settings));

    /// <summary>
    /// UseHostServer modunda CalibraHub'ın kendi şirket bağlantı dizesini çözer.
    /// Diğer modda gereksizdir — çözülmeye çalışılmaz.
    /// </summary>
    private string? ResolveHostConnectionString(ExternalDbConnection settings)
        => settings.UseHostServer
            ? _hostFactory.ResolveConnectionStringForCompany(_hostFactory.ResolveCurrentCompanyId())
            : null;

    private static string Escape(string identifier) => identifier.Replace("]", "]]");

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var idx = text.IndexOf('\n');
        return (idx < 0 ? text : text[..idx]).Trim();
    }
}
