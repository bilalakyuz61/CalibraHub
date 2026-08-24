using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Standart Rehber Kolon Sözleşmesi — TÜM standart rehber view'larının <c>Id</c>, <c>Code</c>
/// ve <c>Name</c> kolonlarını sunduğunu doğrular.
///
/// Bu, projenin yazılı kuralıdır (CLAUDE.md "Standart rehber kuralı"): her ekranda rehberden
/// seçilen değer <c>Code</c>, gösterilen etiket <c>Name</c>'dir. Bir view bu kolonları
/// taşımazsa keşif/normalizasyon 1. ve 2. kolona düşer — rehber "çalışıyor" görünür ama
/// yanlış alanı yazar. Ekranda fark edilmesi zor, veride kalıcı hasar bırakan bir kırıktır.
///
/// Senaryo tek tek rehber saymaz; ortamda TANIMLI olanların hepsini gezer — yeni eklenen bir
/// rehber kuralı bozarsa kendiliğinden yakalanır.
/// </summary>
public sealed class GuideStandardColumnsScenario : FunctionalTestScenarioBase
{
    public const string SampleGuideKey = "sampleGuideCode";

    public override string Key => "GUIDE_STANDARD_COLUMNS";
    public override string Group => "ticari";
    public override string Label => "Standart Rehber Kolon Sözleşmesi";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var (viewsOk, viewsJson) = await StepGetAsync(ctx, steps, "Rehber listesini okuma", "/api/guides/views", ct);
        if (!viewsOk) return;

        // Yalnız standart rehberler (cbv_Guide_*) — özel alan rehberleri bu sözleşmeye tabi değil.
        var names = viewsJson.AsArray()
            .Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : (v.GetStringCI("viewName") ?? v.GetStringCI("name")))
            .Where(n => !string.IsNullOrWhiteSpace(n) && n!.StartsWith("cbv_Guide_", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
        {
            Fail(steps, "Rehber listesini okuma", "Ortamda hiç standart rehber (cbv_Guide_*) bulunamadı.");
            return;
        }
        Pass(steps, "Rehber listesini okuma", $"{names.Count} standart rehber bulundu.");

        var broken = new List<string>();
        foreach (var view in names)
        {
            var res = await ctx.Http.GetAsync($"/api/guides/views/{Uri.EscapeDataString(view!)}/columns", ct);
            if (!res.Ok) { broken.Add($"{view} (kolonlar okunamadı)"); continue; }

            var cols = res.Json.AsArray()
                .Select(c => c.ValueKind == JsonValueKind.String ? c.GetString() : (c.GetStringCI("name") ?? c.GetStringCI("columnName")))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var eksik = new[] { "Id", "Code", "Name" }.Where(k => !cols.Contains(k)).ToList();
            if (eksik.Count > 0) broken.Add($"{view} (eksik: {string.Join(",", eksik)})");
        }

        if (broken.Count > 0)
        {
            Fail(steps, "Kolon sözleşmesi doğrulama",
                $"{broken.Count} rehber Id/Code/Name sözleşmesini karşılamıyor: {string.Join(" · ", broken.Take(6))}" +
                (broken.Count > 6 ? " …" : ""));
            return;
        }
        Pass(steps, "Kolon sözleşmesi doğrulama", $"{names.Count} rehberin tamamı Id/Code/Name taşıyor.");
        ctx.Set(SampleGuideKey, names[0]);
    }
}

/// <summary>
/// Rehber Özelleştirme (Alan Eşleştirme) — bir form alanına rehber bağlanmasını, dönüş/görünüm
/// kolonu ve filtre ayarlarının kaydedilmesini ve RUNTIME tarafından görülmesini sınar.
///
/// Üç ayrı adım, çünkü bu akışın kırığı genelde arada olur: ayar kaydedilir, yönetim ekranında
/// görünür, ama ekranın çalışma anında okuduğu uç onu döndürmez — kullanıcı "kaydettim ama
/// çalışmıyor" der. Bu yüzden hem yönetim okuması hem çalışma-anı okuması ayrı doğrulanır.
/// </summary>
public sealed class GuideCustomizeScenario : FunctionalTestScenarioBase
{
    private const string TargetFormCode = "MATERIAL_CARD_EDIT";

    public override string Key => "GUIDE_CUSTOMIZE";
    public override string Group => "ticari";
    public override string Label => "Rehber Özelleştirme (Alan Eşleştirme)";
    public override IReadOnlyList<string> DependsOn => new[] { "GUIDE_STANDARD_COLUMNS" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var (schemaOk, schemaJson) = await StepGetAsync(ctx, steps, "Form şemasını okuma",
            $"/api/widgets/forms/{TargetFormCode}/schema", ct);
        if (!schemaOk) return;
        var formId = schemaJson.GetIntCI("formId");
        if (formId <= 0) { Fail(steps, "Form şemasını okuma", $"'{TargetFormCode}' için FormId çözülemedi."); return; }

        var viewName = ctx.GetString(GuideStandardColumnsScenario.SampleGuideKey);
        if (string.IsNullOrWhiteSpace(viewName))
        {
            Fail(steps, "Rehber seçimi", "Önceki senaryodan örnek rehber devralınamadı.");
            return;
        }

        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var fieldKey = $"fnRehberAlan{suffix}";
        // Standart rehber sözleşmesi: dönüş Code, görünüm Name (CLAUDE.md). Filtre, cascading
        // token deseniyle yazılır — kaydedilen metnin aynen korunduğu da böyle sınanır.
        const string formatJson = "{\"valueColumn\":\"Code\",\"displayColumn\":\"Name\"}";
        const string filterJson = "{\"IsActive\":\"1\"}";

        var (saveOk, _) = await StepPostAsync(ctx, steps, "Alana rehber bağlama", "/api/field-settings",
            new
            {
                id = 0, formId, fieldKey,
                fieldLabel = $"Fonksiyon Rehber Alanı {suffix}",
                guideCode = (string?)null, viewName,
                filterJson, isRequired = false, formatJson,
                isActive = true, sortOrder = 900,
            }, ct);
        if (!saveOk) return;

        // 1) Yönetim okuması — ayar forma ait listede duruyor mu, içeriği bozulmuş mu.
        var (listOk, listJson) = await StepGetAsync(ctx, steps, "Ayarı yönetim listesinden okuma",
            $"/api/field-settings/form/{formId}", ct);
        if (!listOk) return;
        var row = listJson.AsArray()
            .FirstOrDefault(x => string.Equals(x.GetStringCI("fieldKey"), fieldKey, StringComparison.OrdinalIgnoreCase));
        if (row.ValueKind != JsonValueKind.Object)
        {
            Fail(steps, "Ayarı yönetim listesinden okuma", $"'{fieldKey}' alan ayarı listede bulunamadı.");
            return;
        }
        var savedView = row.GetStringCI("viewName");
        var savedFormat = row.GetStringCI("formatJson") ?? "";
        var savedFilter = row.GetStringCI("filterJson") ?? "";
        if (!string.Equals(savedView, viewName, StringComparison.OrdinalIgnoreCase))
        {
            Fail(steps, "Rehber bağlantısı doğrulama", $"Bağlanan rehber '{viewName}', okunan '{savedView ?? "(boş)"}'.");
            return;
        }
        if (!savedFormat.Contains("\"Code\"", StringComparison.Ordinal) ||
            !savedFormat.Contains("\"Name\"", StringComparison.Ordinal))
        {
            Fail(steps, "Dönüş/görünüm kolonu doğrulama", $"Format ayarı korunmadı: '{savedFormat}'.");
            return;
        }
        if (!savedFilter.Contains("IsActive", StringComparison.Ordinal))
        {
            Fail(steps, "Filtre doğrulama", $"Filtre ayarı korunmadı: '{savedFilter}'.");
            return;
        }
        Pass(steps, "Ayar içeriği doğrulama", $"Rehber={savedView}, dönüş=Code, görünüm=Name, filtre korundu.");

        // 2) Çalışma-anı okuması — ekranın gerçekten kullandığı uç bu bağlantıyı görüyor mu.
        var (rtOk, rtJson) = await StepGetAsync(ctx, steps, "Çalışma anı bağlantılarını okuma",
            $"/api/field-settings/runtime/{TargetFormCode}", ct);
        if (!rtOk) return;
        var runtimeHit = rtJson.AsArray()
            .Any(x => string.Equals(x.GetStringCI("fieldKey"), fieldKey, StringComparison.OrdinalIgnoreCase));
        if (!runtimeHit)
        {
            Fail(steps, "Çalışma anı bağlantılarını okuma",
                $"'{fieldKey}' kaydedildi ama çalışma-anı bağlantılarında yok — ekranda rehber açılmaz.");
            return;
        }
        Pass(steps, "Çalışma anı bağlantılarını okuma", "Bağlantı çalışma anında görünüyor.");
    }
}
