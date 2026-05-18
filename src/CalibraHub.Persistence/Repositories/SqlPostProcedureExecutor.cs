using System.Data;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Persistence.Database;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SQL Server implementasyonu — entegrasyon basariyla calistiktan sonra opsiyonel
/// stored procedure'u parametrize halde calistirir. Per-company DB.
///
/// Parametre cozumlemesi:
///   FormField → headerData (veya linesData ilk satir) icinden alan degeri
///   Constant  → sabit literal
///   RunMeta   → RunId / IntegrationId / StartedAt / SourceRecordId / TriggeredBy
///   Response  → HTTP response body'sinin JSON path'inden deger (basit)
///
/// Guvenlik:
///   - procedureName basit identifier validation (alphanumeric + . + _ + [])
///   - Parameter degerleri SqlParameter olarak gecirilir — SQL injection guvenli
/// </summary>
public sealed class SqlPostProcedureExecutor : IPostProcedureExecutor
{
    private readonly SqlServerConnectionFactory _connectionFactory;

    public SqlPostProcedureExecutor(SqlServerConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PostProcedureExecutionResult> ExecuteAsync(
        string procedureName,
        string? paramsJson,
        IReadOnlyDictionary<string, object?> headerData,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? linesData,
        PostProcedureRunMeta runMeta,
        string? httpResponseBody,
        int? httpStatusCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(procedureName))
            return new PostProcedureExecutionResult(false, 0, "Procedure adi bos.");

        if (!IsValidIdentifier(procedureName))
            return new PostProcedureExecutionResult(false, 0,
                $"Gecersiz procedure adi: '{procedureName}' (sadece harf/rakam/_/. izinli).");

        try
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = procedureName;

            // Parametreleri parse et + value'lari coz
            JsonDocument? doc = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(paramsJson))
                    doc = JsonDocument.Parse(paramsJson);
            }
            catch (Exception jx)
            {
                return new PostProcedureExecutionResult(false, 0,
                    $"PostProcedureParamsJson parse hatasi: {jx.Message}");
            }

            if (doc?.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in doc.RootElement.EnumerateArray())
                {
                    var name       = TryReadString(p, "name");
                    var sourceType = TryReadString(p, "sourceType") ?? "Constant";
                    var sourceVal  = TryReadString(p, "sourceValue");

                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!name.StartsWith("@")) name = "@" + name;

                    object? resolved = ResolveParamValue(
                        sourceType, sourceVal,
                        headerData, linesData, runMeta, httpResponseBody, httpStatusCode);

                    cmd.Parameters.Add(new SqlParameter(name, resolved ?? DBNull.Value));
                }
            }

            doc?.Dispose();

            var rows = await cmd.ExecuteNonQueryAsync(ct);
            return new PostProcedureExecutionResult(true, rows, null);
        }
        catch (Exception ex)
        {
            return new PostProcedureExecutionResult(false, 0, ex.Message);
        }
    }

    private static object? ResolveParamValue(
        string sourceType, string? sourceValue,
        IReadOnlyDictionary<string, object?> headerData,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? linesData,
        PostProcedureRunMeta runMeta,
        string? httpResponseBody,
        int? httpStatusCode)
    {
        switch ((sourceType ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "formfield":
                if (string.IsNullOrWhiteSpace(sourceValue)) return null;
                if (headerData.TryGetValue(sourceValue, out var hv)) return hv;
                // Lines fallback — ilk satir
                if (linesData is { Count: > 0 } && linesData[0].TryGetValue(sourceValue, out var lv)) return lv;
                return null;

            case "runmeta":
                return (sourceValue ?? string.Empty).Trim().ToLowerInvariant() switch
                {
                    "runid"          => runMeta.RunId,
                    "integrationid"  => runMeta.IntegrationId,
                    "startedat"      => runMeta.StartedAt,
                    "sourcerecordid" => runMeta.SourceRecordId,
                    "triggeredby"    => runMeta.TriggeredBy,
                    _                => null,
                };

            case "response":
                // Basit JSON path: "field" veya "nested.field"
                if (string.IsNullOrWhiteSpace(httpResponseBody) || string.IsNullOrWhiteSpace(sourceValue))
                    return null;
                try
                {
                    using var rd = JsonDocument.Parse(httpResponseBody);
                    return ResolveJsonPath(rd.RootElement, sourceValue);
                }
                catch { return null; }

            case "httpstatus":
                return httpStatusCode;

            case "constant":
            default:
                return sourceValue;
        }
    }

    private static object? ResolveJsonPath(JsonElement root, string path)
    {
        var parts = path.Split('.');
        JsonElement cursor = root;
        foreach (var part in parts)
        {
            if (cursor.ValueKind != JsonValueKind.Object) return null;
            if (!cursor.TryGetProperty(part, out var next)) return null;
            cursor = next;
        }
        return cursor.ValueKind switch
        {
            JsonValueKind.String => cursor.GetString(),
            JsonValueKind.Number => cursor.TryGetInt64(out var l) ? (object)l :
                                    cursor.TryGetDecimal(out var d) ? (object)d : cursor.GetDouble(),
            JsonValueKind.True   => true,
            JsonValueKind.False  => false,
            JsonValueKind.Null   => null,
            _                    => cursor.GetRawText(),
        };
    }

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

    /// <summary>
    /// Identifier validasyonu: sadece harf, rakam, _, . ve [ ] izinli (schema.proc veya [schema].[proc]).
    /// </summary>
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
