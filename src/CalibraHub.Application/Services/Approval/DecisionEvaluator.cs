using System.Globalization;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Approval;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Approval;

/// <summary>
/// Faz 4 Runtime — Decision node nodeData JSON'ını <see cref="ApprovalEntityContext"/>'e
/// karşı değerlendir. Multiple rule = AND (hepsi sağlanmalı). Tek kural false → sonuç false.
///
/// Eski document-spesifik switch'ler kaldırıldı; field değer çekme generic
/// dictionary lookup ile yapılır (ctx.HeaderValues / ctx.LineValues).
/// </summary>
public interface IDecisionEvaluator
{
    Task<bool> EvaluateAsync(string? nodeDataJson, ApprovalEntityContext ctx, CancellationToken ct);
}

public sealed class DecisionEvaluator : IDecisionEvaluator
{
    private readonly IApprovalSqlQueryService _sqlSvc;
    private readonly ILogger<DecisionEvaluator> _logger;

    public DecisionEvaluator(IApprovalSqlQueryService sqlSvc, ILogger<DecisionEvaluator> logger)
    {
        _sqlSvc = sqlSvc;
        _logger = logger;
    }

    public async Task<bool> EvaluateAsync(string? nodeDataJson, ApprovalEntityContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nodeDataJson))
            return true; // koşul yok → true

        List<DecisionRule> rules;
        try
        {
            using var doc = JsonDocument.Parse(nodeDataJson);
            if (!doc.RootElement.TryGetProperty("conditionRules", out var rulesEl) ||
                rulesEl.ValueKind != JsonValueKind.Array)
                return true;

            rules = ParseRules(rulesEl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Decision nodeData JSON parse hatası — true varsayılır.");
            return true;
        }

        if (rules.Count == 0) return true;

        // AND: tüm kurallar sağlanmalı
        foreach (var rule in rules)
        {
            bool ok;
            try
            {
                ok = await EvaluateRuleAsync(rule, ctx, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Decision rule değerlendirme hatası ({Field} {Op} {Val}) — false sayıldı.",
                    rule.Field, rule.Op, rule.Value);
                ok = false;
            }
            if (!ok) return false;
        }
        return true;
    }

    private static List<DecisionRule> ParseRules(JsonElement rulesEl)
    {
        var list = new List<DecisionRule>();
        foreach (var el in rulesEl.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var field = TryStr(el, "field") ?? "";
            if (string.IsNullOrWhiteSpace(field)) continue;
            list.Add(new DecisionRule
            {
                Field      = field,
                Op         = TryStr(el, "op") ?? "eq",
                Value      = TryStr(el, "value") ?? "",
                Scope      = TryStr(el, "scope") ?? GuessScope(field),
                SqlMode    = TryStr(el, "sqlMode"),
                SqlQueryId = TryInt(el, "sqlQueryId"),
                SqlText    = TryStr(el, "sqlText"),
            });
        }
        return list;
    }

    private static string GuessScope(string field)
    {
        if (field.StartsWith("line.", StringComparison.OrdinalIgnoreCase)) return "lineAny";
        if (field == "lineCount" || field == "lineMaxTotal" || field == "lineSumQty") return "lineAgg";
        if (field.StartsWith("sql.", StringComparison.OrdinalIgnoreCase)) return "sql";
        return "header";
    }

    private static string? TryStr(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
            : null;

    private static int? TryInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private async Task<bool> EvaluateRuleAsync(DecisionRule rule, ApprovalEntityContext ctx, CancellationToken ct)
    {
        return rule.Scope switch
        {
            "header"  => EvalHeader(rule, ctx),
            "lineAny" => ctx.LineValues.Any(l => EvalLine(rule, l)),
            "lineAll" => ctx.LineValues.Count > 0 && ctx.LineValues.All(l => EvalLine(rule, l)),
            "lineAgg" => EvalHeader(rule, ctx), // agrega alanları header dict'inde durur (entity type doldurur)
            "sql"     => await EvalSqlAsync(rule, ctx, ct),
            _         => false,
        };
    }

    private static bool EvalHeader(DecisionRule rule, ApprovalEntityContext ctx)
    {
        // FlowVariables (SetVariable node çıktıları) öncelikli; yoksa HeaderValues'a düş.
        if (!ctx.FlowVariables.TryGetValue(rule.Field, out var raw))
            ctx.HeaderValues.TryGetValue(rule.Field, out raw);
        return CompareValue(raw, rule.Op, rule.Value);
    }

    private static bool EvalLine(DecisionRule rule, IReadOnlyDictionary<string, object?> line)
    {
        line.TryGetValue(rule.Field, out var raw);
        return CompareValue(raw, rule.Op, rule.Value);
    }

    private async Task<bool> EvalSqlAsync(DecisionRule rule, ApprovalEntityContext ctx, CancellationToken ct)
    {
        string? sql = rule.SqlText;
        if (string.Equals(rule.SqlMode, "library", StringComparison.OrdinalIgnoreCase) && rule.SqlQueryId.HasValue)
        {
            var query = await _sqlSvc.GetByIdAsync(rule.SqlQueryId.Value, ct);
            if (query is null) return false;
            sql = query.SqlText;
        }
        if (string.IsNullOrWhiteSpace(sql)) return false;

        // Standart parametreler + akış değişkenleri (@varAdi olarak kullanılabilir).
        var parameters = new Dictionary<string, object?>(ctx.SqlParameters, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in ctx.FlowVariables)
            if (!parameters.ContainsKey(k)) parameters[k] = v;

        var result = await _sqlSvc.ExecuteAsync(sql, parameters, ct);
        if (!result.Ok)
        {
            _logger.LogWarning("Decision SQL eval başarısız: {Err}", result.Error);
            return false;
        }
        return CompareValue(result.Value, rule.Op, rule.Value);
    }

    // ── Karşılaştırma ─────────────────────────────────────────────────────────
    /// <summary>
    /// Operator desteği: eq, neq, gt, gte, lt, lte, contains, startswith,
    /// endswith, in, notin, between, before, after, isnull, notnull.
    /// </summary>
    internal static bool CompareValue(object? raw, string op, string value)
    {
        op = (op ?? "eq").Trim().ToLowerInvariant();

        switch (op)
        {
            case "isnull":  return raw is null || (raw is string s && string.IsNullOrEmpty(s));
            case "notnull": return !(raw is null || (raw is string s2 && string.IsNullOrEmpty(s2)));
        }

        if (raw is null) return false;

        // Numeric karşılaştırma
        if (TryToDecimal(raw, out var rawNum))
        {
            switch (op)
            {
                case "eq":  return TryToDecimal(value, out var v) && rawNum == v;
                case "neq": return !(TryToDecimal(value, out var v2) && rawNum == v2);
                case "gt":  return TryToDecimal(value, out var v3) && rawNum > v3;
                case "gte": return TryToDecimal(value, out var v4) && rawNum >= v4;
                case "lt":  return TryToDecimal(value, out var v5) && rawNum < v5;
                case "lte": return TryToDecimal(value, out var v6) && rawNum <= v6;
                case "in":
                {
                    var set = SplitCsv(value).Select(t => TryToDecimal(t, out var d) ? (decimal?)d : null).Where(d => d.HasValue).Select(d => d!.Value).ToHashSet();
                    return set.Contains(rawNum);
                }
                case "notin":
                {
                    var set = SplitCsv(value).Select(t => TryToDecimal(t, out var d) ? (decimal?)d : null).Where(d => d.HasValue).Select(d => d!.Value).ToHashSet();
                    return !set.Contains(rawNum);
                }
                case "between":
                {
                    var parts = SplitCsv(value);
                    if (parts.Length < 2) return false;
                    if (!TryToDecimal(parts[0], out var lo) || !TryToDecimal(parts[1], out var hi)) return false;
                    return rawNum >= lo && rawNum <= hi;
                }
            }
        }

        // Date karşılaştırma
        if (raw is DateTime dt || (raw is string ds && DateTime.TryParse(ds, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)))
        {
            DateTime rawDt = raw is DateTime d ? d : DateTime.Parse((string)raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var cmpDt))
            {
                switch (op)
                {
                    case "eq":     return rawDt.Date == cmpDt.Date;
                    case "neq":    return rawDt.Date != cmpDt.Date;
                    case "before":
                    case "lt":     return rawDt < cmpDt;
                    case "lte":    return rawDt <= cmpDt;
                    case "after":
                    case "gt":     return rawDt > cmpDt;
                    case "gte":    return rawDt >= cmpDt;
                }
            }
        }

        // String karşılaştırma (default)
        var rawStr = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "";
        var cmpStr = value ?? "";
        switch (op)
        {
            case "eq":         return string.Equals(rawStr, cmpStr, StringComparison.OrdinalIgnoreCase);
            case "neq":        return !string.Equals(rawStr, cmpStr, StringComparison.OrdinalIgnoreCase);
            case "contains":   return rawStr.Contains(cmpStr, StringComparison.OrdinalIgnoreCase);
            case "startswith": return rawStr.StartsWith(cmpStr, StringComparison.OrdinalIgnoreCase);
            case "endswith":   return rawStr.EndsWith(cmpStr, StringComparison.OrdinalIgnoreCase);
            case "in":         return SplitCsv(cmpStr).Any(t => string.Equals(t, rawStr, StringComparison.OrdinalIgnoreCase));
            case "notin":      return !SplitCsv(cmpStr).Any(t => string.Equals(t, rawStr, StringComparison.OrdinalIgnoreCase));
            default:           return false;
        }
    }

    private static string[] SplitCsv(string s)
        => string.IsNullOrWhiteSpace(s) ? Array.Empty<string>()
        : s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryToDecimal(object? raw, out decimal d)
    {
        switch (raw)
        {
            case decimal dec:  d = dec; return true;
            case int i:        d = i;   return true;
            case long l:       d = l;   return true;
            case double db:    d = (decimal)db; return true;
            case float f:      d = (decimal)f;  return true;
            case string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var sd):
                d = sd; return true;
            case string s2 when decimal.TryParse(s2, NumberStyles.Any, CultureInfo.GetCultureInfo("tr-TR"), out var td):
                d = td; return true;
            default:
                d = 0; return false;
        }
    }
}

internal sealed class DecisionRule
{
    public string Field { get; init; } = "";
    public string Op { get; init; } = "eq";
    public string Value { get; init; } = "";
    public string Scope { get; init; } = "header";
    public string? SqlMode { get; init; }
    public int? SqlQueryId { get; init; }
    public string? SqlText { get; init; }
}
