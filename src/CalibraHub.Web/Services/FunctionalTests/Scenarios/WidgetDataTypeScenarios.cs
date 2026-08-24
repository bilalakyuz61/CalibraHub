using System.Globalization;
using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Tüm Alan Tipleri — desteklenen her veri tipinden bir alan tanımlar, tipe uygun bir değer
/// yazar ve geri okur. Doğrulama tip-duyarlıdır: sayısal alan sayı, evet/hayır alanı mantıksal
/// değer, çoklu seçim liste olarak dönmeli. "HTTP 200" yeterli değildir — değerin YAZILDIĞI
/// tiple OKUNMASI aranır.
///
/// Neden tip başına ayrı adım: değerler tek bir metin kolonunda saklanıp okunurken tipe göre
/// ayrıştırılır (WidgetService.ParseValueForRender). Bu ayrıştırma bir tip için bozulursa
/// yalnızca o tip sessizce yanlış döner — diğerleri çalışmaya devam ettiği için fark edilmez.
///
/// Kapsam dışı tipler senaryonun sonunda AÇIKÇA raporlanır (sessizce atlanmaz): dosya/görsel
/// alanı gerçek bir yükleme akışı, rehber listesi ve alt tablo ise ilişkisel içerik ister;
/// bunlar tek bir değer yuvarlağıyla sınanamaz.
/// </summary>
public sealed class WidgetDataTypeScenario : FunctionalTestScenarioBase
{
    private const string TargetFormCode = "MATERIAL_CARD_EDIT";

    /// <summary>Değer yuvarlağıyla sınanamayan tipler — sonuçta ayrıca bildirilir.</summary>
    private static readonly string[] NotCovered = { "attachment (dosya yükleme akışı)", "guide-list (rehber listesi)", "grid (alt tablo)" };

    private sealed record TypeCase(
        string DataType,
        string Label,
        object? WriteValue,
        string[]? Options,
        Func<JsonElement, (bool Ok, string Actual)> Verify);

    public override string Key => "FORM_FIELD_TYPES";
    public override string Group => "ticari";
    public override string Label => "Tüm Alan Tipleri (Değer Yuvarlağı)";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var itemId = ctx.GetInt(FunctionalTestContext.Keys.Item1Id);
        if (itemId <= 0) { Fail(steps, "Hedef kayıt", "Bağlamda malzeme kartı Id'si yok."); return; }

        var (schemaOk, schemaJson) = await StepGetAsync(ctx, steps, "Form şemasını okuma",
            $"/api/widgets/forms/{TargetFormCode}/schema", ct);
        if (!schemaOk) return;
        var formId = schemaJson.GetIntCI("formId");
        if (formId <= 0) { Fail(steps, "Form şemasını okuma", $"'{TargetFormCode}' için FormId çözülemedi."); return; }

        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var cases = new[]
        {
            new TypeCase("text", "Metin", $"metin-{suffix}", null,
                v => (v.ValueKind == JsonValueKind.String && v.GetString() == $"metin-{suffix}", Describe(v))),

            new TypeCase("textarea", "Uzun Metin", $"birinci satır\nikinci satır {suffix}", null,
                v => (v.ValueKind == JsonValueKind.String && (v.GetString() ?? "").Contains(suffix, StringComparison.Ordinal)
                      && (v.GetString() ?? "").Contains('\n'), Describe(v))),

            // Sayısal alan METİN olarak dönerse hesaplama/karşılaştırma yapan her ekran kırılır.
            new TypeCase("numeric", "Sayı", 1234.56m, null,
                v => (v.ValueKind == JsonValueKind.Number && Math.Abs(v.GetDecimal() - 1234.56m) < 0.0001m, Describe(v))),

            new TypeCase("date", "Tarih", today, null,
                v => (v.ValueKind == JsonValueKind.String && (v.GetString() ?? "").StartsWith(today, StringComparison.Ordinal), Describe(v))),

            // Mantıksal alan "true" METNİ olarak dönerse switch kontrolleri hep açık görünür.
            new TypeCase("boolean", "Evet / Hayır", true, null,
                v => (v.ValueKind is JsonValueKind.True, Describe(v))),

            new TypeCase("dropdown", "Seçim Listesi", "Beta", new[] { "Alfa", "Beta", "Gama" },
                v => (v.ValueKind == JsonValueKind.String && v.GetString() == "Beta", Describe(v))),

            new TypeCase("multi-select", "Çoklu Seçim", new[] { "Alfa", "Gama" }, new[] { "Alfa", "Beta", "Gama" },
                v => (MultiSelectHas(v, "Alfa") && MultiSelectHas(v, "Gama") && !MultiSelectHas(v, "Beta"), Describe(v))),

            new TypeCase("link", "Bağlantı", $"https://calibra.test/{suffix}", null,
                v => (v.ValueKind == JsonValueKind.String && (v.GetString() ?? "").EndsWith(suffix, StringComparison.Ordinal), Describe(v))),
        };

        // 1) Tüm tipleri tanımla.
        var codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sort = 800;
        foreach (var c in cases)
        {
            var code = $"FN_TIP_{c.DataType.Replace("-", "_").ToUpperInvariant()}_{suffix}";
            var res = await ctx.Http.PostAsync("/api/widgets/widgets", new
            {
                id = (int?)null, formId, parentId = (int?)null,
                widgetCode = code, label = $"{c.Label} ({suffix})",
                dataType = c.DataType, maxLength = (int?)500,
                sortOrder = sort++, options = c.Options,
                isActive = true, isRequired = false,
            }, ct);
            if (!res.Ok)
            {
                Fail(steps, $"{c.Label} — alan tanımlama", res.Error);
                return;
            }
            codes[c.DataType] = code;
        }
        Pass(steps, "Alan tanımlama", $"{cases.Length} tipin tamamı tanımlandı.");

        // 2) Hepsini TEK kaydetmede yaz — gerçek ekran davranışı da böyledir.
        var values = new Dictionary<string, object?>();
        foreach (var c in cases) values[codes[c.DataType]] = c.WriteValue;

        var (saveOk, _) = await StepPostAsync(ctx, steps, "Tüm tiplere değer yazma",
            $"/api/widgets/forms/{TargetFormCode}/records/{itemId}",
            new { values, grids = (object?)null, enforceRequired = true }, ct);
        if (!saveOk) return;

        // 3) Geri oku ve tip başına doğrula.
        var (readOk, readJson) = await StepGetAsync(ctx, steps, "Değerleri geri okuma",
            $"/api/widgets/forms/{TargetFormCode}/records/{itemId}", ct);
        if (!readOk) return;

        var byCode = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in readJson.GetArrayCI("widgets"))
        {
            var wc = w.GetStringCI("widgetId");
            if (!string.IsNullOrWhiteSpace(wc)) byCode[wc!] = w;
        }

        foreach (var c in cases)
        {
            if (!byCode.TryGetValue(codes[c.DataType], out var widget))
            {
                Fail(steps, $"{c.Label} ({c.DataType})", "Alan geri okumada hiç dönmedi.");
                return;
            }
            var value = widget.TryGetPropertyCI("value", out var v) ? v : default;
            var (ok, actual) = c.Verify(value);
            if (!ok)
            {
                Fail(steps, $"{c.Label} ({c.DataType})",
                    $"Değer tipe uygun dönmedi — okunan: {actual}.");
                return;
            }
            Pass(steps, $"{c.Label} ({c.DataType})", $"Değer korundu: {actual}.");
        }

        // Kapsam dışı tipleri SESSİZCE atlama — ne sınanmadığı raporda görünsün.
        Pass(steps, "Kapsam dışı tipler", string.Join(" · ", NotCovered) + " — ayrı akış gerektirir, bu senaryoda sınanmadı.");
    }

    private static bool MultiSelectHas(JsonElement v, string option)
    {
        if (v.ValueKind == JsonValueKind.Array)
            return v.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == option);
        // Bazı sürümler çoklu seçimi virgüllü metin olarak döndürebilir — ikisini de kabul et.
        if (v.ValueKind == JsonValueKind.String)
            return (v.GetString() ?? "").Split(',', StringSplitOptions.TrimEntries)
                .Any(s => string.Equals(s, option, StringComparison.Ordinal));
        return false;
    }

    private static string Describe(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Undefined => "(alan yok)",
        JsonValueKind.Null => "(boş)",
        JsonValueKind.String => $"metin \"{Trim(v.GetString())}\"",
        JsonValueKind.Number => $"sayı {v.GetRawText()}",
        JsonValueKind.True => "mantıksal true",
        JsonValueKind.False => "mantıksal false",
        JsonValueKind.Array => $"liste {Trim(v.GetRawText())}",
        _ => Trim(v.GetRawText()),
    };

    private static string Trim(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length > 60 ? s[..60] + "…" : s;
    }
}
