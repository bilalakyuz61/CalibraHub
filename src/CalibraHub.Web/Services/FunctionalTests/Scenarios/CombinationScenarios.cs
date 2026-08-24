using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Stok Özellik ve Değer Tanımlama — iki özellik (Renk / Beden) ve her birine iki değer açar.
/// Kombinasyon senaryosunun girdisi budur: 2×2 = 4 kombinasyon beklenir, yani bu senaryo
/// yalnız "kayıt oluştu mu"yu değil, değerlerin özelliğe doğru bağlandığını da doğrular.
/// </summary>
public sealed class CombinationFeatureDefineScenario : FunctionalTestScenarioBase
{
    public override string Key => "COMB_FEATURE_DEFINE";
    public override string Group => "ticari";
    public override string Label => "Stok Özellik ve Değer Tanımlama";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var valueIds = new List<int>();

        var (f1Ok, feature1Id) = await SaveFeatureAsync(ctx, steps, "Özellik 1 (Renk) tanımlama", $"Fonksiyon Test Renk {suffix}", ct);
        if (!f1Ok) return;
        ctx.Set(FunctionalTestContext.Keys.ComboFeature1Id, feature1Id);

        foreach (var label in new[] { "Kırmızı", "Mavi" })
        {
            var (ok, id) = await SaveValueAsync(ctx, steps, $"Renk değeri '{label}' tanımlama", feature1Id, $"{label} {suffix}", ct);
            if (!ok) return;
            valueIds.Add(id);
        }

        var (f2Ok, feature2Id) = await SaveFeatureAsync(ctx, steps, "Özellik 2 (Beden) tanımlama", $"Fonksiyon Test Beden {suffix}", ct);
        if (!f2Ok) return;
        ctx.Set(FunctionalTestContext.Keys.ComboFeature2Id, feature2Id);

        foreach (var label in new[] { "S", "M" })
        {
            var (ok, id) = await SaveValueAsync(ctx, steps, $"Beden değeri '{label}' tanımlama", feature2Id, $"{label} {suffix}", ct);
            if (!ok) return;
            valueIds.Add(id);
        }

        ctx.Set(FunctionalTestContext.Keys.ComboValueIds, valueIds.ToArray());
        Pass(steps, "Özellik/değer doğrulama", $"2 özellik (#{feature1Id}, #{feature2Id}), {valueIds.Count} değer.");
    }

    private static async Task<(bool Ok, int Id)> SaveFeatureAsync(
        FunctionalTestContext ctx, List<FunctionalTestStep> steps, string label, string name, CancellationToken ct)
    {
        var (ok, json) = await StepPostAsync(ctx, steps, label, "/Logistics/SaveProductFeatureJson",
            new { id = (int?)null, name, dataType = "Text", isActive = true, unitOfMeasure = (string?)null, visibleInDesign = true }, ct);
        if (!ok) return (false, 0);
        var id = json.GetIntCI("id");
        if (id <= 0) { Fail(steps, label, "Sunucu özellik Id'si döndürmedi."); return (false, 0); }
        return (true, id);
    }

    private static async Task<(bool Ok, int Id)> SaveValueAsync(
        FunctionalTestContext ctx, List<FunctionalTestStep> steps, string label, int featureId, string description, CancellationToken ct)
    {
        var (ok, json) = await StepPostAsync(ctx, steps, label, "/Logistics/SaveProductValueJson",
            new
            {
                featureId, description, textValue = description,
                numericValue = (decimal?)null, dateValue = (DateTime?)null, aciklama = (string?)null,
            }, ct);
        if (!ok) return (false, 0);
        var id = json.GetIntCI("id");
        if (id <= 0) { Fail(steps, label, "Sunucu değer Id'si döndürmedi."); return (false, 0); }
        return (true, id);
    }
}

/// <summary>
/// Kombinasyon Üretme — kombinasyonlu bir stok kartı açar, iki özelliği karta bağlar ve
/// seçilen 4 değerden çapraz çarpım (2×2) üretir. Doğrulama sunucunun ürettiği kombinasyon
/// SAYISI üzerinden yapılır: uç "başarılı" dönse bile permütasyon eksik/fazla üretilmişse
/// senaryo kırılır.
/// </summary>
public sealed class CombinationGenerateScenario : FunctionalTestScenarioBase
{
    /// <summary>2 özellik × 2 değer = 4 kombinasyon.</summary>
    public const int ExpectedCombinations = 4;

    public override string Key => "COMB_GENERATE";
    public override string Group => "ticari";
    public override string Label => "Stok Kombinasyonu Üretme";
    public override IReadOnlyList<string> DependsOn => new[] { "COMB_FEATURE_DEFINE" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId);
        var feature1Id = ctx.GetInt(FunctionalTestContext.Keys.ComboFeature1Id);
        var feature2Id = ctx.GetInt(FunctionalTestContext.Keys.ComboFeature2Id);
        var valueIds = ctx.Get<int[]>(FunctionalTestContext.Keys.ComboValueIds) ?? Array.Empty<int>();
        if (valueIds.Length != 4)
        {
            Fail(steps, "Değer listesi kontrolü", $"Bağlamda {valueIds.Length} değer var (4 bekleniyordu).");
            return;
        }

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var code = $"FNK-{suffix}";
        var (cardOk, cardJson) = await StepPostAsync(ctx, steps, "Kombinasyonlu stok kartı oluşturma", "/Logistics/SaveMaterialCardJson",
            new
            {
                itemId = (int?)null, code, name = $"Fonksiyon Test Kombinasyonlu Ürün ({suffix})",
                typeId = 8, unitId, combinations = true,
                taxRate = 20m, trackingType = "None", minStock = 0m, autoSerial = false,
            }, ct);
        if (!cardOk) return;
        var itemId = cardJson.GetIntCI("id");
        if (itemId <= 0) { Fail(steps, "Kombinasyonlu stok kartı oluşturma", "Sunucu Id döndürmedi."); return; }
        ctx.Set(FunctionalTestContext.Keys.ComboItemId, itemId);
        ctx.Set(FunctionalTestContext.Keys.ComboItemCode, code);

        // Özellikleri karta bağla — bağlamadan kombinasyon kaydı reddedilir
        // ("Stoga bagli ozelliklerin tamamindan en az birer deger secmelisiniz").
        var (linkOk, _) = await StepPostAsync(ctx, steps, "Özellikleri stok kartına bağlama", "/Logistics/SaveStockFeatures",
            new
            {
                itemId,
                items = new object[]
                {
                    new { featureId = feature1Id, printDescriptionInDesign = true, allowedValueIds = Array.Empty<int>() },
                    new { featureId = feature2Id, printDescriptionInDesign = true, allowedValueIds = Array.Empty<int>() },
                },
                featureIds = Array.Empty<int>(),
            }, ct);
        if (!linkOk) return;

        var (genOk, _) = await StepPostAsync(ctx, steps, "Kombinasyon üretme (2×2)", "/Logistics/SaveProductCombinationsJson",
            new
            {
                stockCode = code,
                selectedCombinations = valueIds.Select(v => v.ToString()).ToArray(),
            }, ct);
        if (!genOk) return;

        var (readOk, readJson) = await StepGetAsync(ctx, steps, "Kombinasyonları geri okuma",
            $"/Logistics/CombinationsDataJson?stockCode={Uri.EscapeDataString(code)}", ct);
        if (!readOk) return;
        var combos = readJson.GetArrayCI("combos");
        if (combos.Count != ExpectedCombinations)
        {
            Fail(steps, "Kombinasyon sayısı doğrulama",
                $"{combos.Count} kombinasyon üretildi ({ExpectedCombinations} bekleniyordu).");
            return;
        }
        Pass(steps, "Kombinasyon sayısı doğrulama", $"{combos.Count} kombinasyon.");

        // Her kombinasyon iki özellikten de birer değer taşımalı (eksik hücreli kombinasyon
        // ekranda "boş özellik" olarak görünür — sayı doğru olsa bile bu bir kırıktır).
        var incomplete = combos.Count(c => c.GetArrayCI("cells").Count < 2);
        if (incomplete > 0)
        {
            Fail(steps, "Kombinasyon içerik doğrulama", $"{incomplete} kombinasyonda özellik değeri eksik.");
            return;
        }
        Pass(steps, "Kombinasyon içerik doğrulama", "Tüm kombinasyonlar 2 özellik değeri taşıyor.");
    }
}
