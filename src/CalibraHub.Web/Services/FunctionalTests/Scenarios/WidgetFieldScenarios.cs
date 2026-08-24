using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Forma Alan Ekleme (Dinamik Alan / WidgetMas) — kullanıcıların "Alan Yönetimi"nden bir
/// forma özel alan tanımlaması akışını uçtan uca sınar:
///   tanım kaydedilir → form şemasında görünür → kayda değer yazılır → değer geri okunur.
///
/// Neden dört adımın hepsi ayrı ayrı: bu akışın bilinen sessiz kırığı "alan tanımlandı ama
/// ekranda hiç görünmedi" (host mount edilmemiş) ya da "görünüyor ama değer kaydolmuyor"
/// biçiminde ortaya çıkar. Tek bir "kaydetme başarılı" kontrolü ikisini de kaçırır; bu yüzden
/// şema okuması ve değer yuvarlağı ayrı adımlar.
///
/// Hedef form MATERIAL_CARD_EDIT: SEED_MASTER_DATA'nın açtığı gerçek bir malzeme kartı kaydı
/// üzerinde çalışılır, böylece "kayıt yokken de yazıyor gibi görünme" durumu oluşmaz.
/// </summary>
public sealed class WidgetFieldAddScenario : FunctionalTestScenarioBase
{
    private const string TargetFormCode = "MATERIAL_CARD_EDIT";

    public const string WidgetIdKey = "widgetFieldId";
    public const string WidgetCodeKey = "widgetFieldCode";
    public const string WidgetFormIdKey = "widgetFieldFormId";

    public override string Key => "FORM_FIELD_ADD";
    public override string Group => "ticari";
    public override string Label => "Forma Alan Ekleme (Dinamik Alan)";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var itemId = ctx.GetInt(FunctionalTestContext.Keys.Item1Id);
        if (itemId <= 0) { Fail(steps, "Hedef kayıt", "Bağlamda malzeme kartı Id'si yok."); return; }

        var (schemaOk, schemaJson) = await StepGetAsync(ctx, steps, "Form şemasını okuma",
            $"/api/widgets/forms/{TargetFormCode}/schema", ct);
        if (!schemaOk) return;
        var formId = schemaJson.GetIntCI("formId");
        if (formId <= 0) { Fail(steps, "Form şemasını okuma", $"'{TargetFormCode}' formu için FormId çözülemedi."); return; }
        ctx.Set(WidgetFormIdKey, formId);

        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var widgetCode = $"FN_ALAN_{suffix}";
        var label = $"Fonksiyon Test Alanı {suffix}";

        var (addOk, addJson) = await StepPostAsync(ctx, steps, "Özel alan tanımlama", "/api/widgets/widgets",
            new
            {
                id = (int?)null, formId, parentId = (int?)null,
                widgetCode, label, dataType = "text", maxLength = 100,
                sortOrder = 900, options = (string[]?)null, isActive = true,
                isRequired = false,
            }, ct);
        if (!addOk) return;
        var widgetId = addJson.GetIntCI("id");
        if (widgetId <= 0) { Fail(steps, "Özel alan tanımlama", "Sunucu alan Id'si döndürmedi."); return; }
        ctx.Set(WidgetIdKey, widgetId);
        ctx.Set(WidgetCodeKey, widgetCode);

        // Şemada göründü mü — tanım kaydedilip ekranda hiç çıkmama durumunun kanıtı.
        var (reSchemaOk, reSchemaJson) = await StepGetAsync(ctx, steps, "Form şemasını yeniden okuma",
            $"/api/widgets/forms/{TargetFormCode}/schema", ct);
        if (!reSchemaOk) return;
        // DİKKAT: şema ucu alan kodunu `widgetCode` ile döner (WidgetDefinitionDto);
        // KAYIT ucu ise aynı bilgiyi `widgetId` adıyla döner (WidgetRenderDto). İki uç
        // farklı sözleşme kullanıyor — karıştırmak "alan yok" gibi yanlış sonuç verir.
        var inSchema = reSchemaJson.GetArrayCI("widgets")
            .Any(w => string.Equals(w.GetStringCI("widgetCode"), widgetCode, StringComparison.OrdinalIgnoreCase));
        if (!inSchema)
        {
            Fail(steps, "Alanın form şemasında görünmesi",
                $"'{widgetCode}' tanımlandı ama form şemasında yok — ekranda da görünmez.");
            return;
        }
        Pass(steps, "Alanın form şemasında görünmesi", $"'{label}' şemada.");

        // Değer yuvarlağı — yaz, sonra geri oku.
        var value = $"deger-{suffix}";
        var (saveOk, _) = await StepPostAsync(ctx, steps, "Alana değer yazma",
            $"/api/widgets/forms/{TargetFormCode}/records/{itemId}",
            new { values = new Dictionary<string, object?> { [widgetCode] = value }, grids = (object?)null, enforceRequired = true }, ct);
        if (!saveOk) return;

        var (readOk, readJson) = await StepGetAsync(ctx, steps, "Değeri geri okuma",
            $"/api/widgets/forms/{TargetFormCode}/records/{itemId}", ct);
        if (!readOk) return;
        var saved = readJson.GetArrayCI("widgets")
            .FirstOrDefault(w => string.Equals(w.GetStringCI("widgetId"), widgetCode, StringComparison.OrdinalIgnoreCase));
        var savedValue = saved.ValueKind == JsonValueKind.Object ? saved.GetStringCI("value") : null;
        if (!string.Equals(savedValue, value, StringComparison.Ordinal))
        {
            Fail(steps, "Değeri geri okuma", $"Yazılan '{value}', okunan '{savedValue ?? "(boş)"}'.");
            return;
        }
        Pass(steps, "Değeri geri okuma", $"Değer korundu: '{savedValue}'.");
    }
}

/// <summary>
/// Zorunlu Alan Kuralı (Sunucu Tarafı) — özel alan "zorunlu" işaretlendiğinde BOŞ kaydın
/// sunucuda reddedildiğini doğrular.
///
/// Neden sunucu tarafı önemli: zorunluluk yalnız tarayıcıda uygulanırsa kural, isteği
/// doğrudan atan her yol için (mobil, entegrasyon, eski sekme) yok sayılır. Bu senaryo
/// kuralın gerçekten sunucuda dayatıldığını sınar.
///
/// Senaryo kendi izini TEMİZLER: kontrol bittikten sonra alan zorunluluktan çıkarılır —
/// aksi halde aynı formun sonraki her kaydı bu test alanına takılırdı.
/// </summary>
public sealed class WidgetFieldRequiredScenario : FunctionalTestScenarioBase
{
    private const string TargetFormCode = "MATERIAL_CARD_EDIT";

    public override string Key => "FORM_FIELD_REQUIRED";
    public override string Group => "ticari";
    public override string Label => "Zorunlu Alan Kuralı (Sunucu Tarafı)";
    public override IReadOnlyList<string> DependsOn => new[] { "FORM_FIELD_ADD" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var itemId = ctx.GetInt(FunctionalTestContext.Keys.Item1Id);
        var widgetId = ctx.GetInt(WidgetFieldAddScenario.WidgetIdKey);
        var formId = ctx.GetInt(WidgetFieldAddScenario.WidgetFormIdKey);
        var widgetCode = ctx.GetString(WidgetFieldAddScenario.WidgetCodeKey);
        if (widgetId <= 0 || formId <= 0 || string.IsNullOrWhiteSpace(widgetCode))
        {
            Fail(steps, "Alan bilgisi", "Önceki senaryodan alan bilgisi devralınamadı.");
            return;
        }

        var (reqOk, _) = await StepPostAsync(ctx, steps, "Alanı zorunlu yapma", "/api/widgets/widgets",
            new
            {
                id = widgetId, formId, parentId = (int?)null,
                widgetCode, label = "Fonksiyon Test Alanı (zorunlu)", dataType = "text",
                maxLength = 100, sortOrder = 900, options = (string[]?)null,
                isActive = true, isRequired = true,
            }, ct);
        if (!reqOk) return;

        // Boş değerle kaydetme DENEMESİ — reddedilmeli.
        var emptyRes = await ctx.Http.PostAsync(
            $"/api/widgets/forms/{TargetFormCode}/records/{itemId}",
            new { values = new Dictionary<string, object?> { [widgetCode!] = "" }, grids = (object?)null, enforceRequired = true }, ct);
        var enforced = !emptyRes.Ok;

        // Zorunluluğu geri al — senaryo sırası ne olursa olsun form kullanılabilir kalsın.
        var (relaxOk, _) = await StepPostAsync(ctx, steps, "Zorunluluğu geri alma", "/api/widgets/widgets",
            new
            {
                id = widgetId, formId, parentId = (int?)null,
                widgetCode, label = "Fonksiyon Test Alanı", dataType = "text",
                maxLength = 100, sortOrder = 900, options = (string[]?)null,
                isActive = true, isRequired = false,
            }, ct);
        if (!relaxOk) return;

        if (!enforced)
        {
            Fail(steps, "Boş değer reddi",
                "Alan zorunlu olduğu hâlde boş değer kabul edildi — zorunluluk sunucuda dayatılmıyor.");
            return;
        }
        Pass(steps, "Boş değer reddi", "Boş değer sunucuda reddedildi.");
    }
}

/// <summary>
/// Ek Alan Tanımı — TÜM Formlar. Önceki senaryolar akışı tek bir örnek form üzerinde derinlemesine
/// sınar (şema + değer + tip + zorunluluk); bu senaryo ise genişliği kapsar: ek alan kabul eden
/// HER forma bir alan tanımlar ve o formun şemasında göründüğünü doğrular.
///
/// Neden ayrı: bir formun ek alan altyapısı, form kaydı ya da form kodu bozuksa alan sessizce
/// hiç görünmez. Örnek form üzerinden koşan test bunu yalnız o form için yakalar; kırık olan
/// başka bir formsa hiç fark edilmez.
///
/// İlk hatada DURMAZ — bütün formlar denenir ve kırık olanların TAMAMI tek seferde raporlanır;
/// aksi halde her koşuda yalnız bir sonraki kırık ortaya çıkar.
/// </summary>
public sealed class WidgetFieldAllFormsScenario : FunctionalTestScenarioBase
{
    public override string Key => "FORM_FIELD_ALL_FORMS";
    public override string Group => "ticari";
    public override string Label => "Ek Alan Tanımı — Tüm Formlar";
    public override IReadOnlyList<string> DependsOn => new[] { "FORM_FIELD_ADD" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var (formsOk, formsJson) = await StepGetAsync(ctx, steps, "Form listesini okuma", "/api/widgets/forms", ct);
        if (!formsOk) return;

        var forms = formsJson.AsArray()
            .Select(f => (Id: f.GetIntCI("id"), Code: f.GetStringCI("formCode") ?? f.GetStringCI("code")))
            .Where(f => f.Id > 0 && !string.IsNullOrWhiteSpace(f.Code))
            .ToList();

        if (forms.Count == 0)
        {
            Fail(steps, "Form listesini okuma", "Ek alan kabul eden hiçbir form bulunamadı.");
            return;
        }
        Pass(steps, "Form listesini okuma", $"{forms.Count} form ek alan kabul ediyor.");

        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var defineFailed = new List<string>();
        var schemaFailed = new List<string>();

        foreach (var form in forms)
        {
            var code = $"FN_SWEEP_{suffix}";
            var addRes = await ctx.Http.PostAsync("/api/widgets/widgets", new
            {
                id = (int?)null, formId = form.Id, parentId = (int?)null,
                widgetCode = code, label = $"Kapsam Testi Alanı {suffix}",
                dataType = "text", maxLength = 50, sortOrder = 990,
                options = (string[]?)null, isActive = true, isRequired = false,
            }, ct);

            if (!addRes.Ok) { defineFailed.Add($"{form.Code}: {Short(addRes.Error)}"); continue; }

            var schemaRes = await ctx.Http.GetAsync($"/api/widgets/forms/{Uri.EscapeDataString(form.Code!)}/schema", ct);
            var visible = schemaRes.Ok && schemaRes.Json.GetArrayCI("widgets")
                .Any(w => string.Equals(w.GetStringCI("widgetCode"), code, StringComparison.OrdinalIgnoreCase));
            if (!visible) schemaFailed.Add(form.Code!);
        }

        if (defineFailed.Count > 0)
        {
            Fail(steps, "Alan tanımlama (tüm formlar)",
                $"{defineFailed.Count}/{forms.Count} formda alan tanımlanamadı: {string.Join(" · ", defineFailed.Take(5))}" +
                (defineFailed.Count > 5 ? " …" : ""));
            return;
        }
        Pass(steps, "Alan tanımlama (tüm formlar)", $"{forms.Count} formun tamamında alan tanımlandı.");

        if (schemaFailed.Count > 0)
        {
            Fail(steps, "Şemada görünme (tüm formlar)",
                $"{schemaFailed.Count}/{forms.Count} formda tanımlanan alan şemada YOK — o ekranlarda hiç görünmez: " +
                string.Join(", ", schemaFailed.Take(8)) + (schemaFailed.Count > 8 ? " …" : ""));
            return;
        }
        Pass(steps, "Şemada görünme (tüm formlar)", $"{forms.Count} formun tamamında alan şemada göründü.");
    }

    private static string Short(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "?";
        return s.Length > 80 ? s[..80] + "…" : s;
    }
}
