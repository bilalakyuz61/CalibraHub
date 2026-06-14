using System.Globalization;
using System.Security.Claims;
using System.Text;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Persistence.Security;

/// <summary>
/// 2026-06-13 — Satır görünürlük filtresi (OPERATÖR BAZLI kısıtlama modeli).
///
/// **Felsefe:** "Kısıtlanmayan izinlidir." Bir kural (alan + operatör + değer(ler)) tanımlar;
/// kurala TAKILAN — yani operatör koşulunu sağlayan — satırlar HERKESE gizlenir (SystemAdmin hariç).
/// Hiçbir kurala değmeyen satır herkese açıktır. Sahiplik / grant kavramı YOKtur (eski
/// <see cref="DataVisibilityGrant"/> modeli bırakıldı — kullanıcı/departman ayrımı yapılmaz).
///
/// **Kolon kuralı:** doğrudan WHERE predikatı. Aynı alandaki kurallar OR ile, farklı alanlar AND
/// ile birleşir: her alan grubu için <c>AND NOT (&lt;OR'lanmış eşleşme predikatı&gt;)</c> eklenir.
/// Eşleşme predikatları NULL-güvenlidir (<c>F IS NOT NULL AND …</c>) — böylece <c>NOT(…)</c> üç
/// değerli mantıkta NULL satırı yanlışlıkla gizlemez. <c>isnull/isnotnull</c> NULL'ı kasıtlı hedefler.
///
/// **Widget kuralı:** WidgetTra ön-sorgusuyla operatör koşulunu sağlayan RecordId'ler çözülür, sonra
/// <c>alias.[idColumn] NOT IN (yasaklı id'ler)</c> — satır-başı correlated subquery YOK.
///
/// Kurallar 60sn cache'li (company + form bazlı). SystemAdmin → boş predicate (bypass).
/// </summary>
public sealed class SqlDataVisibilityFilter : IDataVisibilityFilter
{
    private readonly IDataVisibilityRuleRepository _ruleRepo;
    private readonly SqlServerConnectionFactory _factory;
    private readonly IHttpContextAccessor _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SqlDataVisibilityFilter>? _logger;
    private readonly string _widgetTraTable;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public SqlDataVisibilityFilter(
        IDataVisibilityRuleRepository ruleRepo,
        SqlServerConnectionFactory factory,
        IHttpContextAccessor http,
        IMemoryCache cache,
        CalibraDatabaseOptions options,
        ILogger<SqlDataVisibilityFilter>? logger = null)
    {
        _ruleRepo = ruleRepo;
        _factory = factory;
        _http = http;
        _cache = cache;
        _logger = logger;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = schema.Replace("]", "]]");
        _widgetTraTable = $"[{s}].[WidgetTra]";
    }

    private static string CacheKey(int companyId, string formCode) =>
        $"dvr:{companyId}:{formCode.ToUpperInvariant()}";

    public void InvalidateCache(string formCode) =>
        _cache.Remove(CacheKey(_factory.ResolveCurrentCompanyId(), formCode));

    private Task<IReadOnlyList<DataVisibilityRule>> GetRulesCachedAsync(string formCode, CancellationToken ct) =>
        _cache.GetOrCreateAsync(CacheKey(_factory.ResolveCurrentCompanyId(), formCode), async entry =>
        {
            entry.SetAbsoluteExpiration(CacheTtl);
            return await _ruleRepo.ListActiveByFormAsync(formCode, ct);
        })!;

    public async Task<DataVisibilityPredicate> BuildAsync(
        string formCode, string tableAlias, string idColumn, CancellationToken ct)
    {
        var httpUser = _http.HttpContext?.User;
        if (httpUser?.Identity?.IsAuthenticated != true)
            return DataVisibilityPredicate.Empty;

        // SystemAdmin → bypass (PermissionService ile tutarlı). Parse başarısızsa bypass yok.
        var roleStr = httpUser.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        if (UserAuthorizationCatalog.TryParseRole(roleStr, out var role) && role == UserRole.SystemAdmin)
            return DataVisibilityPredicate.Empty;

        var allRules = await GetRulesCachedAsync(formCode, ct);
        if (allRules.Count == 0) return DataVisibilityPredicate.Empty;

        // Kısıtlama hedefi (Grants = kural kimin üzerine uygulanacak):
        //   • Grants boşsa → kural herkese uygulanır.
        //   • Grants doluysa → kural SADECE o kullanıcılara uygulanır; diğerleri atlanır.
        var userIdStr = httpUser.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(userIdStr, out var currentUserId);
        IReadOnlyList<DataVisibilityRule> rules = currentUserId > 0
            ? allRules.Where(r =>
                r.Grants.Count == 0 ||
                r.Grants.Any(g => g.UserId.HasValue && g.UserId.Value == currentUserId)).ToList()
            : allRules;
        if (rules.Count == 0) return DataVisibilityPredicate.Empty;

        var sb = new StringBuilder();
        var bag = new ParamBag();
        // Alias boşsa (alias'sız sorgu) prefix kullanma — çıplak [Kolon] üret.
        var alias = tableAlias?.Trim() ?? string.Empty;
        var pfx = alias.Length == 0 ? string.Empty : alias + ".";

        // ── Kolon kuralları: alana göre grupla, her grup OR → tek NOT(...) ekle ──
        foreach (var fieldGroup in rules
                     .Where(r => r.FieldKind == DataVisibilityFieldKind.Column)
                     .GroupBy(r => r.FieldKey, StringComparer.OrdinalIgnoreCase))
        {
            var field = $"{pfx}[{fieldGroup.Key.Replace("]", "]]")}]";
            var orParts = new List<string>();
            foreach (var rule in fieldGroup)
            {
                var pred = BuildColumnMatchPredicate(field, rule, bag);
                if (!string.IsNullOrEmpty(pred)) orParts.Add(pred);
            }
            if (orParts.Count > 0)
                sb.Append($" AND NOT ({string.Join(" OR ", orParts)})");
        }

        // ── Widget kuralları: her kural için eşleşen RecordId'leri çöz (union), id NOT IN ──
        var deniedIds = new HashSet<int>();
        foreach (var rule in rules.Where(r => r.FieldKind == DataVisibilityFieldKind.Widget && r.WidgetId is int))
        {
            foreach (var id in await ResolveDeniedRecordIdsAsync(rule, ct))
                deniedIds.Add(id);
        }
        if (deniedIds.Count > 0)
        {
            var names = deniedIds.Select(id => bag.Add(id)).ToList();
            sb.Append($" AND {pfx}[{idColumn.Replace("]", "]]")}] NOT IN ({string.Join(",", names)})");
        }

        return sb.Length == 0 ? DataVisibilityPredicate.Empty : new DataVisibilityPredicate(sb.ToString(), bag.Params);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Kolon eşleşme predikatı: "bu satır KISITLI mı?" (TRUE → gizlenecek).
    //  Dış katman bunu NOT(...) içine alarak satırı eler. Hepsi NULL-güvenli (2 değerli).
    // ─────────────────────────────────────────────────────────────────────────
    private static string BuildColumnMatchPredicate(string field, DataVisibilityRule rule, ParamBag bag)
    {
        var op = NormOp(rule.Operator);
        switch (op)
        {
            case "isnull":    return $"{field} IS NULL";
            case "isnotnull": return $"{field} IS NOT NULL";
        }

        var values = rule.Values;
        switch (op)
        {
            case "eq":
            case "neq":
            case "gt":
            case "gte":
            case "lt":
            case "lte":
            {
                if (values.Count == 0) return string.Empty;
                var v = ScalarValue(values[0]);
                if (v is null) return string.Empty;
                var sqlOp = op switch
                {
                    "eq" => "=", "neq" => "<>", "gt" => ">", "gte" => ">=", "lt" => "<", "lte" => "<=", _ => "="
                };
                return $"({field} IS NOT NULL AND {field} {sqlOp} {bag.Add(v)})";
            }
            case "between":
            {
                if (values.Count < 2) return string.Empty;
                var v1 = ScalarValue(values[0]);
                var v2 = ScalarValue(values[1]);
                if (v1 is null || v2 is null) return string.Empty;
                return $"({field} IS NOT NULL AND {field} BETWEEN {bag.Add(v1)} AND {bag.Add(v2)})";
            }
            case "in":
            case "not_in":
            {
                var vs = values.Select(ScalarValue).Where(x => x is not null).Select(x => x!).ToList();
                if (vs.Count == 0) return string.Empty;
                var names = vs.Select(bag.Add);
                var inOp = op == "in" ? "IN" : "NOT IN";
                return $"({field} IS NOT NULL AND {field} {inOp} ({string.Join(",", names)}))";
            }
            case "like":
            case "not_like":
            {
                if (values.Count == 0) return string.Empty;
                var s = TextValue(values[0]);
                if (s is null) return string.Empty;
                var likeOp = op == "like" ? "LIKE" : "NOT LIKE";
                return $"({field} IS NOT NULL AND {field} {likeOp} {bag.Add(s)})";
            }
            case "startswith":
            {
                if (values.Count == 0) return string.Empty;
                var s = TextValue(values[0]);
                if (s is null) return string.Empty;
                return $"({field} IS NOT NULL AND {field} LIKE {bag.Add(EscapeLike(s) + "%")})";
            }
            case "endswith":
            {
                if (values.Count == 0) return string.Empty;
                var s = TextValue(values[0]);
                if (s is null) return string.Empty;
                return $"({field} IS NOT NULL AND {field} LIKE {bag.Add("%" + EscapeLike(s))})";
            }
            default:
                // Bilinmeyen operatör → güvenli taraf: kural yokmuş gibi (predikat yok).
                _LogUnknownOp(op);
                return string.Empty;
        }
    }

    /// <summary>
    /// Bir widget kuralı için operatör koşulunu sağlayan (gizlenecek) RecordId'leri TEK indeksli
    /// ön-sorguyla çözer. WidgetId indeksin lider kolonu → seek; Value NVARCHAR(MAX) residual filtre.
    /// </summary>
    private async Task<List<int>> ResolveDeniedRecordIdsAsync(DataVisibilityRule rule, CancellationToken ct)
    {
        if (rule.WidgetId is not int widgetId) return new List<int>();

        var localParams = new List<(string Name, object Value)>();
        int i = 0;
        string Add(object val) { var n = $"@v{i++}"; localParams.Add((n, val)); return n; }

        var valuePred = BuildWidgetMatchPredicate("[Value]", rule, Add);
        if (string.IsNullOrEmpty(valuePred)) return new List<int>();

        var result = new List<int>();
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        foreach (var (n, v) in localParams) cmd.Parameters.AddWithValue(n, v);
        cmd.Parameters.AddWithValue("@w", widgetId);
        cmd.CommandText = $@"
            SELECT DISTINCT [RecordId] FROM {_widgetTraTable}
            WHERE [WidgetId] = @w AND [ParentRecordId] IS NULL
              AND ({valuePred});";

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (!r.IsDBNull(0) && int.TryParse(r.GetString(0), out var id))
                result.Add(id);
        }
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Widget eşleşme predikatı: WidgetTra.Value (NVARCHAR(MAX)) üzerinde "bu kayıt eşleşiyor mu?"
    //  Sonuç TRUE → o RecordId yasaklı → dış sorguda NOT IN ile elenir.
    //  Sayısal operatörler (gt/lt/between) TRY_CONVERT(FLOAT,…) ile sayıya çevirir.
    // ─────────────────────────────────────────────────────────────────────────
    private static string BuildWidgetMatchPredicate(string col, DataVisibilityRule rule, Func<object, string> add)
    {
        var op = NormOp(rule.Operator);
        switch (op)
        {
            case "isnull":    return $"({col} IS NULL OR {col} = N'')";
            case "isnotnull": return $"({col} IS NOT NULL AND {col} <> N'')";
        }

        var values = rule.Values;
        switch (op)
        {
            case "eq":  return values.Count == 0 ? string.Empty : $"{col} = {add(Text(values[0]))}";
            case "neq": return values.Count == 0 ? string.Empty : $"{col} <> {add(Text(values[0]))}";
            case "gt":
            case "gte":
            case "lt":
            case "lte":
            {
                if (values.Count == 0 || !TryNum(values[0], out var num)) return string.Empty;
                var sqlOp = op switch { "gt" => ">", "gte" => ">=", "lt" => "<", "lte" => "<=", _ => "=" };
                return $"TRY_CONVERT(FLOAT, {col}) {sqlOp} {add(num)}";
            }
            case "between":
            {
                if (values.Count < 2 || !TryNum(values[0], out var n1) || !TryNum(values[1], out var n2))
                    return string.Empty;
                return $"TRY_CONVERT(FLOAT, {col}) BETWEEN {add(n1)} AND {add(n2)}";
            }
            case "in":
            case "not_in":
            {
                var vs = values.Select(Text).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();
                if (vs.Count == 0) return string.Empty;
                var names = vs.Select(s => add(s));
                var inOp = op == "in" ? "IN" : "NOT IN";
                return $"{col} {inOp} ({string.Join(",", names)})";
            }
            case "like":
            case "not_like":
            {
                if (values.Count == 0) return string.Empty;
                var likeOp = op == "like" ? "LIKE" : "NOT LIKE";
                return $"{col} {likeOp} {add(Text(values[0]) ?? string.Empty)}";
            }
            case "startswith":
                return values.Count == 0 ? string.Empty
                    : $"{col} LIKE {add(EscapeLike(Text(values[0]) ?? string.Empty) + "%")}";
            case "endswith":
                return values.Count == 0 ? string.Empty
                    : $"{col} LIKE {add("%" + EscapeLike(Text(values[0]) ?? string.Empty))}";
            default:
                _LogUnknownOp(op);
                return string.Empty;
        }
    }

    // ── küçük yardımcılar ────────────────────────────────────────────────────

    private static string NormOp(string? op) => (op ?? "eq").Trim().ToLowerInvariant();

    /// <summary>ID-bazlı hedef varsa int, yoksa string değer (kolon karşılaştırması).</summary>
    private static object? ScalarValue(DataVisibilityRuleValue v) => (object?)v.ValueId ?? v.ValueText;

    /// <summary>String hedef (widget/LIKE) — ValueText, yoksa ValueId metni.</summary>
    private static string? TextValue(DataVisibilityRuleValue v) => v.ValueText ?? v.ValueId?.ToString();

    private static string Text(DataVisibilityRuleValue v) => v.ValueText ?? v.ValueId?.ToString() ?? string.Empty;

    private static bool TryNum(DataVisibilityRuleValue v, out double num) =>
        double.TryParse(Text(v), NumberStyles.Any, CultureInfo.InvariantCulture, out num);

    /// <summary>T-SQL LIKE meta karakterlerini kaçır ([, %, _) — kullanıcı değeri literal eşleşsin.</summary>
    private static string EscapeLike(string s) =>
        s.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");

    private static void _LogUnknownOp(string op)
    {
        // Operatör enum'da yoksa kuralı atlamak güvenlidir (default-allow). Telemetri için sessiz.
    }

    /// <summary>WHERE parametrelerini ardışık adlandıran (@dv0, @dv1…) küçük biriktirici.</summary>
    private sealed class ParamBag
    {
        public readonly List<DataVisibilityParam> Params = new();
        private int _i;
        public string Add(object value)
        {
            var name = $"@dv{_i++}";
            Params.Add(new DataVisibilityParam(name, value));
            return name;
        }
    }
}
