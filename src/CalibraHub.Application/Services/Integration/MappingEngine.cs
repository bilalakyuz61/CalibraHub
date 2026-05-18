using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using NCalc;

namespace CalibraHub.Application.Services.Integration;

/// <summary>
/// MappingEngine — Integration konfigine gore bir form kaydini hedef JSON body'sine cevirir.
///
/// Surec:
///   1. Tum mapping kurallari SortOrder ile sirali alinir
///   2. Her kural icin SourceType'a gore deger cikartilir (FormField/Constant/Formula/Lookup)
///   3. Default + Format uygulanir
///   4. TargetPath JsonObject icinde nested olarak set edilir
///
/// Nested JSON path destegi:
///   "FatUst.CariKod"      → output["FatUst"]["CariKod"] = value (iki seviye)
///   "Kalemler[].StokKod"  → V2 (array support, simdilik tek satir)
///
/// NCalc expression engine:
///   Form alanlari direkt parameter olarak gecirilir.
///   Ornek: "Adet * BirimFiyat * (1 - Iskonto / 100)"
///   Guvenlik: NCalc kod execute etmez, sadece expression parse eder.
/// </summary>
public sealed class MappingEngine : IMappingEngine
{
    private readonly IGuideService _guideService;
    private readonly IIntegrationLookupFunctionRegistry? _functions;

    public MappingEngine(
        IGuideService guideService,
        IIntegrationLookupFunctionRegistry? functions = null)
    {
        _guideService = guideService;
        _functions = functions;
    }

    /// <inheritdoc />
    public Task<JsonObject> BuildAsync(
        global::CalibraHub.Domain.Entities.Integration integration,
        IntegrationSampleRecordDto source,
        CancellationToken ct)
    {
        // Tek-seviyeli (header-only) mapping — header data zaten flat dictionary
        // Master-detail overload'u cagirip linesData/combinationCodes null pas et.
        return BuildAsync(integration, source.FieldValues, linesData: null, combinationCodes: null, ct);
    }

    /// <inheritdoc />
    public async Task<JsonObject> BuildAsync(
        global::CalibraHub.Domain.Entities.Integration integration,
        IReadOnlyDictionary<string, object?> headerData,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? linesData,
        IReadOnlyDictionary<int, string>? combinationCodes,
        CancellationToken ct)
    {
        var output = new JsonObject();

        // SQL Function modu icin standart @P1: kaynak form'un kodu (SourceFormCode).
        // Boylece dbo.fn_X(@formCode, @keyValue, @manualParam) imzasi karsilanir.
        var formCode = integration.SourceFormCode;

        // ── HEADER mapping'leri ─────────────────────────────────────────
        // SourceSection="Header" olanlar (default — eski kayitlar bu sayilir).
        var headerRules = integration.Mappings
            .Where(m => string.Equals(m.SourceSection, "Header", StringComparison.OrdinalIgnoreCase)
                     || string.IsNullOrWhiteSpace(m.SourceSection))
            .OrderBy(m => m.SortOrder);

        foreach (var rule in headerRules)
        {
            var value = await ResolveValueAsync(rule, headerData, formCode, lineCombinationId: null, combinationCodes, ct);
            SetJsonPath(output, rule.TargetPath, value);
        }

        // ── LINES + COMBINATION mapping'leri ────────────────────────────
        // Her line icin ayri context — her array hedef path'e yeni element eklenir.
        if (linesData is { Count: > 0 })
        {
            var lineRules = integration.Mappings
                .Where(m => string.Equals(m.SourceSection, "Lines",       StringComparison.OrdinalIgnoreCase)
                         || string.Equals(m.SourceSection, "Combination", StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.SortOrder)
                .ToList();

            for (var lineIndex = 0; lineIndex < linesData.Count; lineIndex++)
            {
                var line = linesData[lineIndex];
                int? combinationId = TryGetCombinationId(line);

                foreach (var rule in lineRules)
                {
                    var value = await ResolveValueAsync(rule, line, formCode, combinationId, combinationCodes, ct);
                    // Hedef path'in array prefix'ini "Kalems[]" → "Kalems[lineIndex]"e cevir
                    SetJsonPathWithLineIndex(output, rule.TargetPath, lineIndex, value);
                }
            }
        }

        return output;
    }

    /// <summary>
    /// Bir kuralin degerini cozumler — kaynak tipi + section'a gore source data farkli.
    /// Combination section: lineCombinationId varsa combinationCodes'tan kod cekilir.
    /// </summary>
    private async Task<object?> ResolveValueAsync(
        IntegrationMapping rule,
        IReadOnlyDictionary<string, object?> data,
        string? formCode,
        int? lineCombinationId,
        IReadOnlyDictionary<int, string>? combinationCodes,
        CancellationToken ct)
    {
        object? value;

        // Combination section ozel: source field "Code" sentetik — runtime resolver
        if (string.Equals(rule.SourceSection, "Combination", StringComparison.OrdinalIgnoreCase)
            && rule.SourceType == IntegrationSourceType.FormField
            && string.Equals(rule.SourceValue, "Code", StringComparison.OrdinalIgnoreCase))
        {
            value = lineCombinationId.HasValue && combinationCodes is not null
                  && combinationCodes.TryGetValue(lineCombinationId.Value, out var code)
                ? code : null;
        }
        else
        {
            value = rule.SourceType switch
            {
                IntegrationSourceType.FormField => ReadDictField(data, rule.SourceValue),
                IntegrationSourceType.Constant  => rule.SourceValue,
                IntegrationSourceType.Formula   => EvaluateFormulaFromDict(rule.SourceValue, data),
                IntegrationSourceType.Lookup    => await ResolveLookupFromDictAsync(rule, data, ct),
                IntegrationSourceType.Function  => await ResolveFunctionFromDictAsync(rule, data, formCode, ct),
                _                                => null,
            };
        }

        // Null/bos ise default
        if (IsNullOrEmpty(value)) value = rule.DefaultValue;

        // Format donusumu
        return ApplyFormat(value, rule.TargetDataType, rule.FormatPattern);
    }

    private static int? TryGetCombinationId(IReadOnlyDictionary<string, object?> line)
    {
        // DocumentLine.CombinationId — v_Flat_*Lines view'inda oldugu gibi gelmeli
        if (line.TryGetValue("CombinationId", out var v) && v is not null)
        {
            if (v is int i) return i;
            if (v is long l && l <= int.MaxValue) return (int)l;
            if (v is decimal d) return (int)d;
            if (int.TryParse(v.ToString(), out var p)) return p;
        }
        return null;
    }

    private static object? ReadDictField(IReadOnlyDictionary<string, object?> data, string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName)) return null;
        return data.TryGetValue(fieldName, out var v) ? v : null;
    }

    private static object? EvaluateFormulaFromDict(string? expression, IReadOnlyDictionary<string, object?> data)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;
        try
        {
            var e = new Expression(expression);
            foreach (var (k, v) in data)
                e.Parameters[k] = v;
            return e.Evaluate();
        }
        catch { return null; }
    }

    /// <summary>
    /// Fonksiyon source tipi resolver — iki yol:
    ///   A) SourceValue "schema.fn" (nokta iceriyor) → YENI: DB'de tanimli scalar function direkt cagrilir
    ///      (admin "Lookup Fonksiyonu" tablosuna kayit gerekmez — kullanici DB tarafinda function yazar).
    ///      Imza: SELECT [schema].[fn](@P1=formCode, @P2=keyValue, @P3=manualParam)
    ///   B) SourceValue legacy code (orn. "ITEMS", "CONTACTS", "CARI_BAKIYE") → wrapper tablosundan
    ///      cozumlenir (geriye uyum: IntegrationLookupFunction kayitlari hala calisir).
    /// </summary>
    private async Task<object?> ResolveFunctionFromDictAsync(
        IntegrationMapping rule, IReadOnlyDictionary<string, object?> data,
        string? formCode, CancellationToken ct)
    {
        if (_functions is null) return null;
        if (string.IsNullOrWhiteSpace(rule.SourceValue)) return null;

        // Form alanindan @P2 degerini oku — zorunlu degil (function imzasi @P2 = NULL alabilir)
        string? keyValue = null;
        if (!string.IsNullOrWhiteSpace(rule.LookupSourceField)
            && data.TryGetValue(rule.LookupSourceField, out var v))
        {
            keyValue = v?.ToString();
        }

        // Yol A: SourceValue nokta iceriyorsa direkt DB function (sema.fn formati) — wrapper by-pass
        if (rule.SourceValue.Contains('.'))
        {
            return await _functions.ExecuteDbFunctionAsync(
                functionFullName: rule.SourceValue,
                formCode:         formCode,
                keyValue:         keyValue,
                manualParam:      rule.LookupParam,
                ct:               ct);
        }

        // Yol B: Legacy wrapper kodu (ITEMS / CONTACTS / vb.) — eski mapping'ler icin
        return await _functions.ResolveWithParamsAsync(
            functionId:   rule.SourceValue,
            formCode:     formCode,
            keyValue:     keyValue,
            manualParam:  rule.LookupParam,
            returnColumn: rule.LookupReturnColumn,
            ct:           ct);
    }

    private async Task<object?> ResolveLookupFromDictAsync(
        IntegrationMapping rule, IReadOnlyDictionary<string, object?> data, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rule.SourceValue)) return null;

        // LookupFiltersJson coklu WHERE filtre + LookupReturnColumn — zenginlestirilmis akis.
        // Bos ise basit (geriye uyum) akis: LookupSourceField + ResolveAsync(value).
        var constraints = ParseLookupFilters(rule.LookupFiltersJson, data);

        // Eski (legacy) tek-anahtar akis: SADECE filter YOK + LookupSourceField dolu ise calisir.
        // Yeni UI hem filter hem LookupSourceField yaziyor (geriye uyum icin) — eger filter
        // ZATEN ayni source field icin WHERE iceriyorsa, burada ValueColumn'a ekstra constraint
        // ekleyince "WHERE Id = X AND Code = X" gibi imkansiz kosul olusur → 0 satir. Bu yuzden
        // filter doluysa LookupSourceField'i tamamen atla (filter zaten dogru islerini yapiyor).
        if (constraints.Count == 0 && !string.IsNullOrWhiteSpace(rule.LookupSourceField))
        {
            var keyValue = data.TryGetValue(rule.LookupSourceField, out var v) ? v?.ToString() : null;
            if (!string.IsNullOrWhiteSpace(keyValue))
            {
                // Geriye uyum: filter yoksa ResolveAsync (single value lookup) — performansli yol.
                if (string.IsNullOrWhiteSpace(rule.LookupReturnColumn))
                {
                    var resolved = await _guideService.ResolveAsync(rule.SourceValue, keyValue, ct);
                    return resolved?.Display ?? keyValue;
                }

                // ReturnColumn set ama filter yok — ValueColumn'a constraint olarak ekle ki SearchAsync calsisin.
                var schema = await _guideService.GetSchemaAsync(rule.SourceValue, ct);
                var valueCol = schema?.ValueColumn ?? "Code";
                constraints.Add(new GuideConstraintDto(
                    Field: valueCol, Operator: "eq", Value: keyValue, Logic: "and"));
            }
        }

        if (constraints.Count == 0) return null; // hicbir kosul yok, sonuc deterministic degil

        // Coklu filtre yolu: SearchAsync ilk satiri dondursun, sonra LookupReturnColumn'a gore deger ceker.
        var result = await _guideService.SearchAsync(
            rule.SourceValue, search: null, page: 1, pageSize: 1,
            sortColumn: null, sortDirection: null, ct, constraints: constraints);

        var row = result?.Rows?.FirstOrDefault();
        if (row is null) return null;

        // LookupReturnColumn set ise o kolonu Cells'tan oku; degilse standart Display
        if (!string.IsNullOrWhiteSpace(rule.LookupReturnColumn)
            && row.Cells is not null
            && row.Cells.TryGetValue(rule.LookupReturnColumn, out var cellVal))
        {
            return cellVal;
        }

        return row.Display ?? row.Value;
    }

    /// <summary>
    /// LookupFiltersJson'i GuideConstraintDto listesine cevirir. sourceField doluysa
    /// data dictionary'sinden degeri okur; value doluysa literal kullanir.
    /// Hata durumunda bos liste — runtime gurultu kirilmasi yerine grace fall-back.
    /// </summary>
    private static List<GuideConstraintDto> ParseLookupFilters(
        string? filtersJson, IReadOnlyDictionary<string, object?> data)
    {
        var list = new List<GuideConstraintDto>();
        if (string.IsNullOrWhiteSpace(filtersJson)) return list;

        try
        {
            using var doc = JsonDocument.Parse(filtersJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                string? field    = TryReadString(el, "field");
                string? oper     = TryReadString(el, "operator");
                string? logic    = TryReadString(el, "logic") ?? "and";
                string? rawSql   = TryReadString(el, "rawSql");
                string? srcField = TryReadString(el, "sourceField");
                string? litValue = TryReadString(el, "value");

                // RawSql modu: dogrudan ekle (admin trusted)
                if (!string.IsNullOrWhiteSpace(rawSql))
                {
                    list.Add(new GuideConstraintDto(RawSql: rawSql, Logic: logic));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(field)) continue;

                // sourceField doluysa runtime data'dan oku; degilse literal value
                string? resolvedValue;
                if (!string.IsNullOrWhiteSpace(srcField))
                {
                    resolvedValue = data.TryGetValue(srcField, out var dv) ? dv?.ToString() : null;
                }
                else
                {
                    resolvedValue = litValue;
                }

                if (string.IsNullOrWhiteSpace(resolvedValue)) continue; // bos kosulu eklemenin anlami yok

                list.Add(new GuideConstraintDto(
                    Field: field,
                    Operator: string.IsNullOrWhiteSpace(oper) ? "eq" : oper,
                    Value: resolvedValue,
                    Logic: logic));
            }
        }
        catch
        {
            // bozuk JSON — sessizce yut, runtime crash etme
        }

        return list;
    }

    private static string? TryReadString(JsonElement el, string property)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(property, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number => p.ToString(),
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            JsonValueKind.Null   => null,
            _                    => p.ToString(),
        };
    }

    /// <summary>
    /// Hedef path'te "Kalems[]" gibi array prefix varsa, ilgili line index ile değiştirir
    /// ("Kalems[0]" gibi) ve SetJsonPath'e yönlendirir. Array prefix yoksa (yani Lines
    /// mapping'i array hedefine değil, header'a yazıyorsa) standart SetJsonPath çağrılır.
    /// </summary>
    private static void SetJsonPathWithLineIndex(JsonObject root, string path, int lineIndex, object? value)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        // "Kalems[].StokKodu" → first[] segment'i bul
        var arrayMarkerIdx = path.IndexOf("[]", StringComparison.Ordinal);
        if (arrayMarkerIdx < 0)
        {
            // Array prefix yok — standart nested set (lines mapping ama header gibi davranir)
            SetJsonPath(root, path, value);
            return;
        }

        // "Kalems[].StokKodu" parts:
        //   prefix      = "Kalems"
        //   afterArray  = ".StokKodu"
        var prefix     = path.Substring(0, arrayMarkerIdx);
        var afterArray = path.Substring(arrayMarkerIdx + 2);
        if (afterArray.StartsWith(".", StringComparison.Ordinal))
            afterArray = afterArray.Substring(1);

        // Root altinda "Kalems" array'i bul/yarat
        if (root[prefix] is not JsonArray arr)
        {
            arr = new JsonArray();
            root[prefix] = arr;
        }

        // lineIndex'e kadar array'i doldur (her line icin yeni JsonObject)
        while (arr.Count <= lineIndex)
            arr.Add(new JsonObject());

        if (arr[lineIndex] is not JsonObject lineObj)
        {
            lineObj = new JsonObject();
            arr[lineIndex] = lineObj;
        }

        // afterArray bos ise ("Kalems[]" tek basina) tum line'i value yap — nadir
        if (string.IsNullOrEmpty(afterArray))
        {
            arr[lineIndex] = ToJsonNode(value) ?? new JsonObject();
            return;
        }

        // Line obje icinde nested SetJsonPath uygula
        SetJsonPath(lineObj, afterArray, value);
    }

    // ── Format ─────────────────────────────────────────────────────────

    public static object? ApplyFormat(object? value, string? targetDataType, string? formatPattern)
    {
        if (value is null) return null;

        // Once tipe gore parse
        var dt = (targetDataType ?? string.Empty).ToLowerInvariant();

        switch (dt)
        {
            case "date":
            case "datetime":
                if (TryToDateTime(value, out var d))
                {
                    var pattern = string.IsNullOrWhiteSpace(formatPattern) ? "yyyy-MM-ddTHH:mm:ss" : formatPattern;
                    return d.ToString(pattern, CultureInfo.InvariantCulture);
                }
                return value;

            case "decimal":
            case "numeric":
            case "money":
                if (TryToDecimal(value, out var dec))
                {
                    var pattern = string.IsNullOrWhiteSpace(formatPattern) ? "F2" : formatPattern;
                    return decimal.Parse(dec.ToString(pattern, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                }
                return value;

            case "int":
            case "integer":
                if (TryToInt(value, out var i)) return i;
                return value;

            case "bool":
            case "boolean":
                return TryToBool(value, out var b) ? b : value;

            case "string":
            case "text":
            default:
                var s = value.ToString() ?? string.Empty;
                return formatPattern?.ToLowerInvariant() switch
                {
                    "upper" => s.ToUpperInvariant(),
                    "lower" => s.ToLowerInvariant(),
                    "trim"  => s.Trim(),
                    _       => s,
                };
        }
    }

    // ── JSON path setter ───────────────────────────────────────────────

    /// <summary>
    /// "FatUst.CariKod" gibi nested path icin output icinde JsonObject zinciri olusturup
    /// son segmenete value yazar. Tek seviye ise (orn. "CariKod") direkt root'a yazilir.
    ///
    /// Array path destegi ("Kalemler[].StokKod") V2 — su anda nokta ile ayrilmis duz path.
    /// </summary>
    public static void SetJsonPath(JsonObject root, string path, object? value)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var parts = path.Split('.');
        JsonObject cursor = root;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            var segment = parts[i];
            if (cursor[segment] is not JsonObject child)
            {
                child = new JsonObject();
                cursor[segment] = child;
            }
            cursor = child;
        }

        var lastSegment = parts[^1];
        cursor[lastSegment] = ToJsonNode(value);
    }

    private static JsonNode? ToJsonNode(object? value) => value switch
    {
        null                         => null,
        string s                     => JsonValue.Create(s),
        int i                        => JsonValue.Create(i),
        long l                       => JsonValue.Create(l),
        decimal de                   => JsonValue.Create(de),
        double d                     => JsonValue.Create(d),
        float f                      => JsonValue.Create(f),
        bool b                       => JsonValue.Create(b),
        DateTime dt                  => JsonValue.Create(dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
        DateTimeOffset dto           => JsonValue.Create(dto.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)),
        Guid g                       => JsonValue.Create(g.ToString()),
        JsonNode jn                  => jn,
        _                            => JsonValue.Create(value.ToString()),
    };

    // ── Parse helpers ──────────────────────────────────────────────────

    private static bool IsNullOrEmpty(object? value) =>
        value is null
        || (value is string s && string.IsNullOrEmpty(s))
        || value is DBNull;

    private static bool TryToDateTime(object value, out DateTime result)
    {
        switch (value)
        {
            case DateTime d:
                result = d;
                return true;
            case DateTimeOffset dto:
                result = dto.UtcDateTime;
                return true;
            case string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed):
                result = parsed;
                return true;
            case string s2 when DateTime.TryParse(s2, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.AssumeLocal, out var parsed2):
                result = parsed2;
                return true;
        }
        result = default;
        return false;
    }

    private static bool TryToDecimal(object value, out decimal result)
    {
        switch (value)
        {
            case decimal dec: result = dec; return true;
            case double d:    result = (decimal)d; return true;
            case float f:     result = (decimal)f; return true;
            case int i:       result = i; return true;
            case long l:      result = l; return true;
            case string s:    return TryParseDecimalString(s, out result);
        }
        result = 0m;
        return false;
    }

    /// <summary>
    /// String'ten decimal parse — bug fix: oncesinde invariant culture once denenirken
    /// "1234,56" girdiyi NumberStyles.Any virgulu binlik ayirici sayip 123456 dondurdu.
    /// Yeni mantik: karakterlere bakip uygun culture'i sec, sonra parse et.
    /// Kural:
    ///   "1234.56"       -> invariant (nokta decimal)
    ///   "1234,56"       -> tr-TR    (virgul decimal)
    ///   "1.234,56"      -> tr-TR    (nokta binlik, virgul decimal)
    ///   "1,234.56"      -> invariant (virgul binlik, nokta decimal)
    ///   "1234567"       -> invariant
    /// </summary>
    internal static bool TryParseDecimalString(string s, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var trimmed = s.Trim();

        var hasComma = trimmed.Contains(',');
        var hasDot   = trimmed.Contains('.');

        CultureInfo culture;
        if (hasComma && hasDot)
        {
            // Son gelen karakter decimal separator'dur — onu cultureyi belirler
            var lastComma = trimmed.LastIndexOf(',');
            var lastDot   = trimmed.LastIndexOf('.');
            culture = lastComma > lastDot
                ? CultureInfo.GetCultureInfo("tr-TR")   // virgul son -> tr decimal
                : CultureInfo.InvariantCulture;          // nokta son  -> en decimal
        }
        else if (hasComma)
        {
            // Sadece virgul -> Turkce decimal
            culture = CultureInfo.GetCultureInfo("tr-TR");
        }
        else
        {
            // Sadece nokta veya hicbiri -> invariant
            culture = CultureInfo.InvariantCulture;
        }

        return decimal.TryParse(trimmed, NumberStyles.Any, culture, out result);
    }

    private static bool TryToInt(object value, out int result)
    {
        switch (value)
        {
            case int i:    result = i; return true;
            case long l when l >= int.MinValue && l <= int.MaxValue:
                result = (int)l; return true;
            case decimal de: result = (int)de; return true;
            case double d:   result = (int)d; return true;
            case string s when int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                result = parsed; return true;
        }
        result = 0;
        return false;
    }

    private static bool TryToBool(object value, out bool result)
    {
        switch (value)
        {
            case bool b: result = b; return true;
            case int i:  result = i != 0; return true;
            case string s when bool.TryParse(s, out var parsed): result = parsed; return true;
            case string s2 when s2.Equals("1", StringComparison.Ordinal) || s2.Equals("yes", StringComparison.OrdinalIgnoreCase) || s2.Equals("on", StringComparison.OrdinalIgnoreCase):
                result = true; return true;
            case string s3 when s3.Equals("0", StringComparison.Ordinal) || s3.Equals("no", StringComparison.OrdinalIgnoreCase) || s3.Equals("off", StringComparison.OrdinalIgnoreCase):
                result = false; return true;
        }
        result = false;
        return false;
    }
}
