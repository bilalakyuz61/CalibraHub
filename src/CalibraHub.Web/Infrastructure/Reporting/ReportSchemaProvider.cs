using CalibraHub.Persistence.Database;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Web.Infrastructure.Reporting;

/// <summary>
/// Belirtilen SQL view'daki kolonlari INFORMATION_SCHEMA'dan okur.
/// Designer .frx acilirken bu sema TableDataSource olarak enjekte edilir.
///
/// Cache: Kolonlar nadiren degistigi icin view basina 60 sn hafizada tutulur.
/// </summary>
public sealed class ReportSchemaProvider
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private static readonly Dictionary<string, (DateTime, IReadOnlyList<ReportColumnSchema>)> _cache = new();
    private static readonly object _lock = new();
    private const string DefaultViewName = "vw_ReportDocument";
    private const int CacheSeconds = 60;

    public ReportSchemaProvider(SqlServerConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Varsayilan view (vw_ReportDocument) icin semayi doner. Geriye uyumluluk icin saklandi.
    /// </summary>
    public Task<IReadOnlyList<ReportColumnSchema>> GetDocumentSchemaAsync(CancellationToken ct)
        => GetSchemaAsync(DefaultViewName, ct);

    /// <summary>
    /// Verilen view adi icin kolon listesini doner. Gecersiz isim (regex fail) veya
    /// bulunmayan view icin bos liste doner, exception atmaz.
    /// </summary>
    public async Task<IReadOnlyList<ReportColumnSchema>> GetSchemaAsync(
        string viewName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(viewName)) viewName = DefaultViewName;

        // Guvenlik: sadece alphanum + underscore, maks 150 char
        if (viewName.Length > 150 ||
            !System.Text.RegularExpressions.Regex.IsMatch(viewName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            return Array.Empty<ReportColumnSchema>();
        }

        var cacheKey = viewName;
        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var entry)
                && (DateTime.UtcNow - entry.Item1).TotalSeconds < CacheSeconds)
            {
                return entry.Item2;
            }
        }

        var list = new List<ReportColumnSchema>();

        try
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COLUMN_NAME, DATA_TYPE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @ViewName
                ORDER BY ORDINAL_POSITION;
                """;
            cmd.Parameters.Add(new SqlParameter("@ViewName", viewName));

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var name = r.GetString(0);
                var sqlType = r.GetString(1);
                list.Add(new ReportColumnSchema(name, MapSqlTypeToDotNet(sqlType)));
            }
        }
        catch
        {
            // View yoksa bos sema don — Designer acilir ama alan gostermez.
        }

        lock (_lock)
        {
            _cache[cacheKey] = (DateTime.UtcNow, list);
        }

        return list;
    }

    private static string MapSqlTypeToDotNet(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "int" or "tinyint" or "smallint" => "System.Int32",
        "bigint"                          => "System.Int64",
        "bit"                             => "System.Boolean",
        "decimal" or "numeric" or "money" or "smallmoney" => "System.Decimal",
        "float"                           => "System.Double",
        "real"                            => "System.Single",
        "datetime" or "datetime2" or "smalldatetime" or "date" => "System.DateTime",
        "time"                            => "System.TimeSpan",
        "uniqueidentifier"                => "System.Guid",
        _                                 => "System.String",
    };
}
