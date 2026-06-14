using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;

namespace CalibraHub.Application.Workflow;

/// <summary>
/// Belge bazlı NCalc context snapshot üretir. Instance başlatılırken bir kez çağrılır.
/// Sonraki evaluate çağrılarında WorkflowInstance.ContextJson parse edilir — repo'ya gidilmez.
/// </summary>
public sealed class DocumentContextBuilder(IDocumentRepository documentRepository) : IDocumentContextBuilder
{
    public async Task<Dictionary<string, object?>> BuildContextAsync(int documentId, CancellationToken ct = default)
    {
        var doc   = await documentRepository.GetByIdAsync(documentId, ct);
        var lines = await documentRepository.GetLinesAsync(documentId, ct);

        if (doc is null) return [];

        var distinctItems = lines.Select(l => l.ItemId).Distinct().Count();

        return new Dictionary<string, object?>
        {
            ["Amount"]        = (double)doc.GrandTotal,
            ["SubTotal"]      = (double)doc.SubTotal,
            ["CurrencyId"]    = doc.CurrencyId,
            ["Currency"]      = doc.CurrencyCode,      // workflow expression backward compat
            ["LineCount"]     = lines.Count,
            ["DistinctItems"] = distinctItems,
            // IsExport: TRY DEGIL ise ihracat. CurrencyCode null gelirse defansif false.
            ["IsExport"]      = !string.IsNullOrEmpty(doc.CurrencyCode)
                                  && !string.Equals(doc.CurrencyCode, "TRY", StringComparison.OrdinalIgnoreCase),
            ["ContactCode"]   = doc.ContactCode ?? "",
            ["CreatedById"]   = (object?)(doc.CreatedById?.ToString() ?? ""),
        };
    }

    public static Dictionary<string, object?> ParseSnapshot(string? contextJson)
    {
        if (string.IsNullOrWhiteSpace(contextJson)) return [];
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(contextJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            return raw.ToDictionary(kv => kv.Key, kv => ToClrValue(kv.Value));
        }
        catch { return []; }
    }

    private static object? ToClrValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.TryGetInt64(out var i) ? i : (object?)el.GetDouble(),
        JsonValueKind.String => el.GetString(),
        JsonValueKind.True   => true,
        JsonValueKind.False  => false,
        JsonValueKind.Null   => null,
        _                    => el.GetRawText(),
    };
}
