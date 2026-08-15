using System.Data;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Persistence.Services;

/// <summary>
/// 2026-08-15 — İçe aktarım öncesi/sonrası stored procedure çalıştırıcısı.
/// Hedef DB iş tanımında prosedür başına seçilir: CalibraHub şirket DB'si (varsayılan)
/// veya harici kaynak DB (kaynağa yazmanın tek yolu — örn. "aktarıldı" işaretleme).
///
/// Parametre kaynakları (paramsJson dizisi, her öğe {name, sourceType, sourceValue}):
///   Constant → sabit literal
///   RunMeta  → RunId / JobId / JobName / StartedAt / TriggeredBy
///   Stats    → RowsRead / RowsInserted / RowsUpdated / RowsFailed (yalnız son prosedürde dolu)
///
/// Güvenlik: prosedür adı identifier doğrulamasından geçer; değerler SqlParameter ile
/// bağlanır. Hata yutulmaz — loglanır ve sonuca taşınır.
/// </summary>
public sealed class SqlDataImportProcedureExecutor : IDataImportProcedureExecutor
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly ILogger<SqlDataImportProcedureExecutor> _logger;

    public SqlDataImportProcedureExecutor(
        SqlServerConnectionFactory connectionFactory,
        ILogger<SqlDataImportProcedureExecutor> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<DataImportProcedureResult> ExecuteAsync(
        string procedureName,
        DataImportProcedureTarget target,
        ExternalDbConnection? sourceConnection,
        string? paramsJson,
        DataImportProcedureContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(procedureName))
            return new DataImportProcedureResult(false, 0, "Prosedür adı boş.");

        if (!IsValidIdentifier(procedureName))
            return new DataImportProcedureResult(false, 0,
                $"Geçersiz prosedür adı: '{procedureName}' (yalnız harf/rakam/_/. izinli).");

        if (target == DataImportProcedureTarget.Source && sourceConnection is null)
            return new DataImportProcedureResult(false, 0,
                "Kaynak DB hedefli prosedür için kaynak bağlantı tanımı gereklidir.");

        List<(string Name, string SourceType, string? SourceValue)> parameters;
        try
        {
            parameters = ParseParams(paramsJson);
        }
        catch (JsonException jx)
        {
            return new DataImportProcedureResult(false, 0, $"Prosedür parametre JSON'u okunamadı: {jx.Message}");
        }

        try
        {
            await using var conn = target == DataImportProcedureTarget.Source
                ? await ExternalDbConnectionStringBuilder.OpenAsync(
                      sourceConnection!, "CalibraHub.DataImport.Procedure", ct)
                : await _connectionFactory.OpenConnectionAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = procedureName;
            cmd.CommandTimeout = target == DataImportProcedureTarget.Source && sourceConnection is not null
                ? sourceConnection.CommandTimeoutSeconds
                : 120;

            foreach (var (name, sourceType, sourceValue) in parameters)
            {
                var paramName = name.StartsWith('@') ? name : "@" + name;
                var resolved = ResolveParamValue(sourceType, sourceValue, context);
                cmd.Parameters.Add(new SqlParameter(paramName, resolved ?? DBNull.Value));
            }

            var rows = await cmd.ExecuteNonQueryAsync(ct);
            return new DataImportProcedureResult(true, rows, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "İçe aktarım prosedürü başarısız. Prosedür={Procedure} Hedef={Target} İş={JobName} RunId={RunId}",
                procedureName, target, context.JobName, context.RunId);
            return new DataImportProcedureResult(false, 0, ex.Message);
        }
    }

    private static List<(string, string, string?)> ParseParams(string? paramsJson)
    {
        var result = new List<(string, string, string?)>();
        if (string.IsNullOrWhiteSpace(paramsJson)) return result;

        using var doc = JsonDocument.Parse(paramsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

        foreach (var p in doc.RootElement.EnumerateArray())
        {
            var name = TryReadString(p, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            result.Add((name, TryReadString(p, "sourceType") ?? "Constant", TryReadString(p, "sourceValue")));
        }
        return result;
    }

    private static object? ResolveParamValue(string sourceType, string? sourceValue, DataImportProcedureContext c)
        => (sourceType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "runmeta" => (sourceValue ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "runid"       => c.RunId,
                "jobid"       => c.JobId,
                "jobname"     => c.JobName,
                "startedat"   => c.StartedAt,
                "triggeredby" => c.TriggeredBy,
                _             => null,
            },
            "stats" => (sourceValue ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "rowsread"     => c.RowsRead,
                "rowsinserted" => c.RowsInserted,
                "rowsupdated"  => c.RowsUpdated,
                "rowsfailed"   => c.RowsFailed,
                _              => null,
            },
            _ => sourceValue,
        };

    private static string? TryReadString(JsonElement el, string property)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(property, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Null   => null,
            _                    => p.ToString(),
        };
    }

    /// <summary>Yalnız harf, rakam, _, . ve [ ] izinli (schema.proc veya [schema].[proc]).</summary>
    private static bool IsValidIdentifier(string name)
    {
        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '[' || c == ']'))
                return false;
        }
        return name.Length > 0 && name.Length <= 200;
    }
}
