using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Application.Services.Integration;

/// <summary>
/// 2026-05-22 Pre-flight Filter Engine — Integration.SourceFilterJson'u parse edip
/// hem SQL WHERE clause hem de in-memory boolean check üretir.
///
/// JSON formatı (basit AND zinciri, ileride OR/grup eklenebilir):
///   [
///     { "field": "Status",            "op": "eq",      "value": "Approved" },
///     { "field": "ContactId",         "op": "notnull"                       },
///     { "field": "GrandTotal",        "op": "gte",     "value": "1000"      },
///     { "field": "widget:cariKodu",   "op": "notnull"                       },
///     { "field": "widget:onayDurumu", "op": "eq",      "value": "Onayli"    }
///   ]
///
/// Operators: eq, neq, gt, gte, lt, lte, isnull, notnull, contains, startsWith, in, between
/// Özel prefix: 'widget:fieldKey' = WidgetTra (EAV) tablosundan değer JOIN ile çekilir.
/// "logic" alanı şu an ignore edilir — tüm kurallar AND ile birleştirilir.
///
/// **TEK NOKTADAN** kullanım:
///   - Queue listesi/sayaçlar → BuildSqlWhere ile WHERE'e inject
///   - Runner → EvaluateRecord ile in-memory check (defense in depth)
///   - Manuel buton → API'den (form-field değerleri ile EvaluateRecord) check
///
/// Tüm tetikleyici yollar (Manual/Cron/OnSave/Queue/Sayaç) bunu kullanır.
/// </summary>
public sealed class IntegrationFilterEngine
{
    public IReadOnlyList<FilterRule> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<FilterRule>();
        try
        {
            var raw = JsonSerializer.Deserialize<JsonElement>(json);
            if (raw.ValueKind != JsonValueKind.Array) return Array.Empty<FilterRule>();
            var list = new List<FilterRule>();
            foreach (var el in raw.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var field = ReadString(el, "field");
                var op    = ReadString(el, "op");
                var value = ReadString(el, "value");
                if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(op)) continue;
                list.Add(new FilterRule(field.Trim(), op.Trim().ToLowerInvariant(), value));
            }
            return list;
        }
        catch { return Array.Empty<FilterRule>(); }
    }

    /// <summary>
    /// In-memory check — record verisi sözlüğüne karşı kuralları değerlendirir.
    /// Runner ve manuel buton guard için kullanılır.
    /// Record null/boş ise tüm kurallar fail → false.
    /// </summary>
    public bool EvaluateRecord(string? filterJson, IReadOnlyDictionary<string, object?>? record)
    {
        var rules = Parse(filterJson);
        if (rules.Count == 0) return true;   // filtre yok → her şey geçer
        if (record is null || record.Count == 0) return false;
        // Case-insensitive lookup
        var ciRecord = new Dictionary<string, object?>(record, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (!EvalSingle(rule, ciRecord)) return false;   // AND — ilk fail → false
        }
        return true;
    }

    /// <summary>
    /// SQL WHERE clause + parametre listesi üretir.
    /// Caller bunu kendi SELECT'ine "(... AND {result.WhereClause})" şeklinde ekler.
    /// Boş döndüyse filtre yok → caller hiçbir ek WHERE eklemez.
    ///
    /// **Widget filtresi**: 'widget:fieldKey' prefix'i otomatik LEFT JOIN üretir,
    /// caller `result.JoinClauses` ekler. Widget tablosu: <c>WidgetTra wt</c>
    /// (RecordId = baseTable.PK, FieldKey = ...).
    ///
    /// Whitelist: field adı `^[A-Za-z_][A-Za-z0-9_:]{0,127}$` regex'ine uyar
    /// (SQL injection güvenliği — admin yazsa bile keyfi SQL atamaz).
    /// </summary>
    public BuildSqlResult BuildSqlWhere(
        string? filterJson,
        string baseTableAlias,
        string schema,
        string baseTable,
        string baseRecordKeyCol,
        string formCode)
    {
        var rules = Parse(filterJson);
        if (rules.Count == 0) return BuildSqlResult.Empty;

        var whereParts = new List<string>();
        var joinParts  = new List<string>();
        var parameters = new List<SqlParameter>();
        var widgetJoinIdx = 0;

        foreach (var rule in rules)
        {
            string? colExpr;
            if (rule.Field.StartsWith("widget:", StringComparison.OrdinalIgnoreCase))
            {
                var widgetKey = rule.Field.Substring("widget:".Length).Trim();
                if (!IsSafeIdent(widgetKey)) continue;
                widgetJoinIdx++;
                var alias = $"wt{widgetJoinIdx}";
                var keyParamName = $"@__wk{widgetJoinIdx}";
                var fcParamName  = $"@__wf{widgetJoinIdx}";
                joinParts.Add(
                    $"LEFT JOIN [{schema}].[WidgetTra] {alias} " +
                    $"ON {alias}.[RecordId] = CAST({baseTableAlias}.[{baseRecordKeyCol}] AS NVARCHAR(100)) " +
                    $"AND {alias}.[FieldKey] = {keyParamName} " +
                    $"AND {alias}.[FormCode] = {fcParamName}");
                parameters.Add(new SqlParameter(keyParamName, widgetKey));
                parameters.Add(new SqlParameter(fcParamName, formCode));
                colExpr = $"{alias}.[Value]";
            }
            else if (IsSafeIdent(rule.Field))
            {
                colExpr = $"{baseTableAlias}.[{rule.Field}]";
            }
            else
            {
                // Güvensiz alan adı — kuralı atla
                continue;
            }

            var pName = $"@__f{parameters.Count}";
            var sqlFragment = BuildOpFragment(colExpr, rule.Op, rule.Value, pName, parameters);
            if (sqlFragment != null) whereParts.Add(sqlFragment);
        }

        if (whereParts.Count == 0) return BuildSqlResult.Empty;
        return new BuildSqlResult(
            WhereClause: "(" + string.Join(" AND ", whereParts) + ")",
            JoinClauses: joinParts.Count == 0 ? "" : string.Join(' ', joinParts),
            Parameters:  parameters);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static bool EvalSingle(FilterRule rule, IDictionary<string, object?> record)
    {
        // widget: prefix in-memory için: record'ta aynı key'le aramayı dene
        var key = rule.Field;
        var stripped = key.StartsWith("widget:", StringComparison.OrdinalIgnoreCase)
            ? key.Substring("widget:".Length)
            : key;

        record.TryGetValue(key, out var val);
        if (val is null) record.TryGetValue(stripped, out val);

        var s = val is null or DBNull ? null : Convert.ToString(val, CultureInfo.InvariantCulture);
        return rule.Op switch
        {
            "isnull"     => string.IsNullOrEmpty(s),
            "notnull"    => !string.IsNullOrEmpty(s),
            "eq"         => string.Equals(s, rule.Value, StringComparison.OrdinalIgnoreCase),
            "neq"        => !string.Equals(s, rule.Value, StringComparison.OrdinalIgnoreCase),
            "contains"   => s != null && rule.Value != null && s.Contains(rule.Value, StringComparison.OrdinalIgnoreCase),
            "startswith" => s != null && rule.Value != null && s.StartsWith(rule.Value, StringComparison.OrdinalIgnoreCase),
            "gt"         => TryCompareDecimal(s, rule.Value, out var c) && c > 0,
            "gte"        => TryCompareDecimal(s, rule.Value, out var c) && c >= 0,
            "lt"         => TryCompareDecimal(s, rule.Value, out var c) && c < 0,
            "lte"        => TryCompareDecimal(s, rule.Value, out var c) && c <= 0,
            "in"         => InMatch(s, rule.Value),
            "between"    => BetweenMatch(s, rule.Value),
            _            => true,   // bilinmeyen operatör — geç (defansif)
        };
    }

    private static bool TryCompareDecimal(string? a, string? b, out int cmp)
    {
        cmp = 0;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        if (!decimal.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var da)) return false;
        if (!decimal.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var db)) return false;
        cmp = da.CompareTo(db);
        return true;
    }

    private static bool InMatch(string? s, string? csv)
    {
        if (s == null || csv == null) return false;
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
            if (string.Equals(s, p, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool BetweenMatch(string? s, string? range)
    {
        if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(range)) return false;
        var parts = range.Split(',', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        if (!decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var ds)) return false;
        if (!decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var lo)) return false;
        if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var hi)) return false;
        return ds >= lo && ds <= hi;
    }

    private static string? BuildOpFragment(string colExpr, string op, string? value, string pName, List<SqlParameter> parameters)
    {
        switch (op)
        {
            case "isnull":
                return $"({colExpr} IS NULL OR LEN(CAST({colExpr} AS NVARCHAR(MAX))) = 0)";
            case "notnull":
                return $"({colExpr} IS NOT NULL AND LEN(CAST({colExpr} AS NVARCHAR(MAX))) > 0)";
            case "eq":
                parameters.Add(new SqlParameter(pName, (object?)value ?? DBNull.Value));
                return $"{colExpr} = {pName}";
            case "neq":
                parameters.Add(new SqlParameter(pName, (object?)value ?? DBNull.Value));
                return $"{colExpr} <> {pName}";
            case "contains":
                parameters.Add(new SqlParameter(pName, "%" + (value ?? "") + "%"));
                return $"{colExpr} LIKE {pName}";
            case "startswith":
                parameters.Add(new SqlParameter(pName, (value ?? "") + "%"));
                return $"{colExpr} LIKE {pName}";
            case "gt":
                parameters.Add(new SqlParameter(pName, (object?)value ?? DBNull.Value));
                return $"TRY_CAST({colExpr} AS DECIMAL(18,4)) > TRY_CAST({pName} AS DECIMAL(18,4))";
            case "gte":
                parameters.Add(new SqlParameter(pName, (object?)value ?? DBNull.Value));
                return $"TRY_CAST({colExpr} AS DECIMAL(18,4)) >= TRY_CAST({pName} AS DECIMAL(18,4))";
            case "lt":
                parameters.Add(new SqlParameter(pName, (object?)value ?? DBNull.Value));
                return $"TRY_CAST({colExpr} AS DECIMAL(18,4)) < TRY_CAST({pName} AS DECIMAL(18,4))";
            case "lte":
                parameters.Add(new SqlParameter(pName, (object?)value ?? DBNull.Value));
                return $"TRY_CAST({colExpr} AS DECIMAL(18,4)) <= TRY_CAST({pName} AS DECIMAL(18,4))";
            case "in":
                // CSV → IN (@p_0, @p_1, ...)
                var parts = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 0) return null;
                var pnames = new List<string>();
                for (int i = 0; i < parts.Length; i++)
                {
                    var pn = $"{pName}_{i}";
                    parameters.Add(new SqlParameter(pn, parts[i]));
                    pnames.Add(pn);
                }
                return $"{colExpr} IN (" + string.Join(',', pnames) + ")";
            case "between":
                var rparts = (value ?? "").Split(',', 2, StringSplitOptions.TrimEntries);
                if (rparts.Length != 2) return null;
                var lo = $"{pName}_lo"; var hi = $"{pName}_hi";
                parameters.Add(new SqlParameter(lo, rparts[0]));
                parameters.Add(new SqlParameter(hi, rparts[1]));
                return $"TRY_CAST({colExpr} AS DECIMAL(18,4)) BETWEEN TRY_CAST({lo} AS DECIMAL(18,4)) AND TRY_CAST({hi} AS DECIMAL(18,4))";
            default:
                return null;
        }
    }

    private static string? ReadString(JsonElement obj, string prop)
    {
        if (obj.TryGetProperty(prop, out var v))
        {
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.True   => "true",
                JsonValueKind.False  => "false",
                JsonValueKind.Null   => null,
                _                    => v.GetRawText(),
            };
        }
        return null;
    }

    private static readonly System.Text.RegularExpressions.Regex SafeIdentRegex =
        new("^[A-Za-z_][A-Za-z0-9_]{0,127}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static bool IsSafeIdent(string s) => SafeIdentRegex.IsMatch(s);
}

public sealed record FilterRule(string Field, string Op, string? Value);

public sealed record BuildSqlResult(
    string WhereClause,
    string JoinClauses,
    IReadOnlyList<SqlParameter> Parameters)
{
    public static readonly BuildSqlResult Empty = new("", "", Array.Empty<SqlParameter>());
    public bool IsEmpty => string.IsNullOrEmpty(WhereClause);
}
