using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Özellik / Değer / Stok-Özellik içe aktarımı — üç şablonun ZİNCİRİNİ sınar
/// (Özellik → Değer → Stok Özelliği). Sıra önemli: her adım bir öncekinin ürettiği
/// kaydı ADINA göre çözer, kimlik numarasına göre değil.
///
/// Sınanan üç davranış, üçü de sessiz kırılmaya açık olduğu için ayrı ayrı:
///   1. Değer, özelliğin VERİ TİPİNE göre doğru kolona yazılıyor mu — sayısal özelliğe
///      metin verildiğinde satır REDDEDİLMELİ. Kabul edilseydi değer metin kolonuna
///      kaçar, ekranda boş görünür ama aktarım "başarılı" sayılırdı.
///   2. Aynı stok+özellik için İKİ değer satırı geldiğinde ikisi de kalmalı. Yazma yolu
///      (SetFeaturesForItemAsync) bir stoğun listesini tamamen yeniden yazdığı için,
///      naif uygulamada ikinci satır birincisini SİLERDİ — bu testin asıl hedefi budur.
///   3. Değeri boş bırakılan satır "özellik bağlı, değer serbest" olarak kurulmalı.
///
/// Dosya CSV olarak gönderilir; gerçek ekran Excel de kabul eder ama sınanan şey
/// ayrıştırıcı değil handler davranışıdır.
/// </summary>
public sealed class ImportFeatureScenario : FunctionalTestScenarioBase
{
    public override string Key => "IMPORT_FEATURES";
    public override string Group => "ticari";
    public override string Label => "İçe Aktarım: Özellik / Değer / Stok Özelliği";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var itemId = ctx.GetInt(FunctionalTestContext.Keys.Item1Id);
        var itemCode = ctx.GetString(FunctionalTestContext.Keys.Item1Code);
        if (itemId <= 0 || string.IsNullOrWhiteSpace(itemCode))
        {
            Fail(steps, "Hedef malzeme", "Bağlamda malzeme kartı bilgisi yok.");
            return;
        }

        var sfx = Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        var renk = $"Renk {sfx}";
        var uzunluk = $"Uzunluk {sfx}";

        // ── 1) Özellik ──────────────────────────────────────────────────────
        var featureCsv = "Özellik Adı;Veri Tipi\n" +
                         $"{renk};Metin\n" +
                         $"{uzunluk};Sayı\n";
        var (fOk, fJson) = await CommitAsync(ctx, steps, "Özellik aktarımı", "FEATURE",
            new[] { ("Name", "Özellik Adı"), ("DataType", "Veri Tipi") },
            "Name", featureCsv, ct);
        if (!fOk) return;
        if (Inserted(fJson) != 2)
        {
            Fail(steps, "Özellik aktarımı", $"2 özellik beklenirken {Inserted(fJson)} eklendi ({Summary(fJson)}).");
            return;
        }
        Pass(steps, "Özellik aktarımı", "2 özellik eklendi (metin + sayısal).");

        // ── 2) Değer — biri KASITLI hatalı ─────────────────────────────────
        // "uzun" sayısal özelliğe verilemez; o satırın reddedilmesi beklenir.
        var valueCsv = "Özellik Adı;Değer\n" +
                       $"{renk};Kırmızı\n" +
                       $"{renk};Mavi\n" +
                       $"{uzunluk};120\n" +
                       $"{uzunluk};uzun\n";
        var (vOk, vJson) = await CommitAsync(ctx, steps, "Değer aktarımı", "FEATURE_VALUE",
            new[] { ("FeatureName", "Özellik Adı"), ("Value", "Değer") },
            "FeatureName", valueCsv, ct);
        if (!vOk) return;

        var vIns = Inserted(vJson);
        var vFail = Failed(vJson);
        if (vIns != 3 || vFail != 1)
        {
            Fail(steps, "Değer aktarımı",
                $"3 geçerli + 1 reddedilen beklenirken {vIns} eklendi / {vFail} reddedildi ({Summary(vJson)}). " +
                "Sayısal özelliğe metin değer kabul edilmiş olabilir.");
            return;
        }
        Pass(steps, "Değer aktarımı", "3 değer eklendi; sayısal özelliğe verilen metin değer reddedildi.");

        // ── 3) Stok–Özellik: aynı özellik için İKİ değer + değersiz bir satır ──
        var mapCsv = "Malzeme Kodu;Özellik Adı;Değer\n" +
                     $"{itemCode};{renk};Kırmızı\n" +
                     $"{itemCode};{renk};Mavi\n" +
                     $"{itemCode};{uzunluk};\n";
        var (mOk, mJson) = await CommitAsync(ctx, steps, "Stok özelliği aktarımı", "ITEM_FEATURE",
            new[] { ("MaterialCode", "Malzeme Kodu"), ("FeatureName", "Özellik Adı"), ("Value", "Değer") },
            "MaterialCode", mapCsv, ct);
        if (!mOk) return;
        if (Failed(mJson) > 0)
        {
            Fail(steps, "Stok özelliği aktarımı", $"Satır(lar) reddedildi: {Summary(mJson)}");
            return;
        }
        Pass(steps, "Stok özelliği aktarımı", "3 satır işlendi.");

        // ── 4) DOĞRULAMA: iki değer de duruyor mu ──────────────────────────
        var (rOk, rJson) = await StepGetAsync(ctx, steps, "Stok özelliklerini geri okuma",
            $"/Logistics/GetStockFeatures?stockCardId={itemId}", ct);
        if (!rOk) return;

        var features = rJson.GetArrayCI("features").ToList();
        var renkNode = features.FirstOrDefault(f =>
            string.Equals(f.GetStringCI("name"), renk, StringComparison.OrdinalIgnoreCase));
        var uzunlukNode = features.FirstOrDefault(f =>
            string.Equals(f.GetStringCI("name"), uzunluk, StringComparison.OrdinalIgnoreCase));

        if (renkNode.ValueKind != JsonValueKind.Object || uzunlukNode.ValueKind != JsonValueKind.Object)
        {
            Fail(steps, "Stok özelliklerini geri okuma", "Aktarılan özellikler stok kartında bulunamadı.");
            return;
        }

        // GetBoolCI bool? döner — alan hiç yoksa null; "true değil" hepsini kapsar.
        if (renkNode.GetBoolCI("linked") != true || uzunlukNode.GetBoolCI("linked") != true)
        {
            Fail(steps, "Özellik bağlantısı", "Özellikler stok kartına bağlanmamış.");
            return;
        }

        // ASIL SINAV: iki değerli satırın ikisi de kalmalı. 1 dönerse ikinci satır
        // birincisini ezmiş demektir (tam-değiştirme davranışı sızmış).
        var renkValueCount = renkNode.GetArrayCI("allowedValueIds").Count;
        if (renkValueCount != 2)
        {
            Fail(steps, "Çok değerli eşleştirme",
                $"'{renk}' için 2 izinli değer beklenirken {renkValueCount} bulundu — " +
                "ikinci satır birincisini silmiş olabilir.");
            return;
        }
        Pass(steps, "Çok değerli eşleştirme", "Aynı özelliğe iki değer birlikte korundu.");

        // Değeri boş satır: özellik bağlı ama değer kısıtı YOK.
        var uzunlukValueCount = uzunlukNode.GetArrayCI("allowedValueIds").Count;
        if (uzunlukValueCount != 0)
        {
            Fail(steps, "Değersiz eşleştirme",
                $"Değer kolonu boş bırakılmıştı; kısıt beklenmezken {uzunlukValueCount} izinli değer var.");
            return;
        }
        Pass(steps, "Değersiz eşleştirme", "Değer boş bırakılan özellik kısıtsız bağlandı.");
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────────

    /// <summary>
    /// Tek adımda içe aktarım: kolon eşlemesi kurulur, CSV yüklenir, commit edilir.
    /// Şablon KAYDEDİLMEZ (Id=0) — test ortamda kalıcı şablon bırakmamalı.
    /// </summary>
    private static async Task<(bool Ok, JsonElement Json)> CommitAsync(
        FunctionalTestContext ctx, List<FunctionalTestStep> steps, string label,
        string entity, (string Target, string Source)[] columns, string matchKey,
        string csv, CancellationToken ct)
    {
        var spec = JsonSerializer.Serialize(new
        {
            id = 0,
            name = "Fonksiyon Testi",
            targetEntity = entity,
            sheetName = (string?)null,
            headerRowIndex = 1,
            matchKeyField = matchKey,
            columns = columns.Select(c => new { targetKey = c.Target, sourceColumn = c.Source, transform = (string?)null }),
            isActive = true,
        });

        var res = await ctx.Http.PostFileAsync("/Import/api/commit", "test.csv", csv,
            new Dictionary<string, string> { ["spec"] = spec }, ct);

        if (!res.Ok)
        {
            Fail(steps, label, res.Error ?? "İçe aktarım başarısız.");
            return (false, default);
        }
        return (true, res.Json);
    }

    private static int Inserted(JsonElement json) => json.GetIntCI("inserted");
    private static int Failed(JsonElement json) => json.GetIntCI("failed");

    /// <summary>Rapor için kısa özet — hata mesajı tek başına teşhis ettirmeli.</summary>
    private static string Summary(JsonElement json)
        => $"eklendi={json.GetIntCI("inserted")} güncellendi={json.GetIntCI("updated")} " +
           $"hata={json.GetIntCI("failed")} atlandı={json.GetIntCI("skipped")}";
}
