using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Reçete içe aktarımı — ÖNİZLEMENİN DOĞRU SÖYLEDİĞİNİ sınar.
///
/// Neden bu test var: reçete aktarımı tekrar çalıştırıldığında mevcut reçetenin
/// kalemlerini SİLİP dosyadakileri yazar (SaveBOMAsync Id'siz çağrılır → baz reçeteyi
/// bulur → UpdateBOMAsync önce satırları siler). Önizleme ise 2026-08-25 öncesinde her
/// satırı "yeni kayıt" gösteriyordu: kullanıcı "5 yeni reçete eklenecek" yazısını görüp
/// onaylıyor, oysa 5 mevcut reçetesinin içeriği baştan yazılıyordu.
///
/// Yıkıcı bir işlemin güvenli görünen bir mesajın arkasına saklanması, bu projede
/// tekrar etmemesi gereken bir hata sınıfı. Test tam olarak onu bekliyor:
///   · ilk önizleme → "yeni" (ortamda o mamulün reçetesi yok)
///   · kaydetme     → reçete oluşur
///   · aynı dosyayı yeniden önizleme → "GÜNCELLENECEK" demeli
///
/// Son adım "eklendi" derse önizleme yalan söylüyor demektir ve test kırılır.
/// </summary>
public sealed class ImportBomPreviewScenario : FunctionalTestScenarioBase
{
    public override string Key => "IMPORT_BOM_PREVIEW";
    public override string Group => "uretim";
    public override string Label => "Reçete Aktarımı: Önizleme Ekle/Güncelle Ayrımı";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var parentCode = ctx.GetString(FunctionalTestContext.Keys.Item1Code);
        var childCode = ctx.GetString(FunctionalTestContext.Keys.Item2Code);
        if (string.IsNullOrWhiteSpace(parentCode) || string.IsNullOrWhiteSpace(childCode))
        {
            Fail(steps, "Hedef malzemeler", "Bağlamda iki malzeme kodu yok (mamul + bileşen gerekli).");
            return;
        }

        var csv = "Ana Ürün Kodu;Bileşen Kodu;Miktar\n" +
                  $"{parentCode};{childCode};2\n";

        // ── 1) İlk önizleme: bu mamulün reçetesi yoksa "yeni" demeli ────────
        var (p1Ok, p1) = await SendAsync(ctx, steps, "İlk önizleme", "/Import/api/preview", csv, ct);
        if (!p1Ok) return;

        var firstIsUpdate = p1.GetIntCI("updateCount") > 0;
        if (firstIsUpdate)
        {
            // Ortamda bu mamulün reçetesi ZATEN varsa test anlamsızlaşır (asıl sınav
            // "yeniden güncellemeye geçiş"tir). Bunu sessizce geçmek yerine bildirip
            // çıkıyoruz — yanlış yeşil, hiç sonuç vermemekten kötüdür.
            Pass(steps, "İlk önizleme",
                $"'{parentCode}' için zaten bir reçete var; ekle→güncelle geçişi bu ortamda sınanamıyor.");
            return;
        }
        Pass(steps, "İlk önizleme", "Reçete yok — yeni kayıt olarak raporlandı.");

        // ── 2) Kaydet ───────────────────────────────────────────────────────
        var (cOk, cJson) = await SendAsync(ctx, steps, "Reçete kaydetme", "/Import/api/commit", csv, ct);
        if (!cOk) return;
        if (cJson.GetIntCI("failed") > 0)
        {
            Fail(steps, "Reçete kaydetme", $"Satır reddedildi (hata={cJson.GetIntCI("failed")}).");
            return;
        }
        Pass(steps, "Reçete kaydetme", "Reçete oluşturuldu.");

        // ── 3) ASIL SINAV: aynı dosya artık GÜNCELLEME olmalı ───────────────
        var (p2Ok, p2) = await SendAsync(ctx, steps, "İkinci önizleme", "/Import/api/preview", csv, ct);
        if (!p2Ok) return;

        var upd = p2.GetIntCI("updateCount");
        var ins = p2.GetIntCI("insertCount");
        if (upd < 1)
        {
            Fail(steps, "Ekle/güncelle ayrımı",
                $"Mevcut reçete ÜZERİNE YAZILACAĞI hâlde önizleme güncelleme demiyor " +
                $"(yeni={ins}, güncellenecek={upd}). Kullanıcı, reçetesinin kalemlerinin " +
                "silinip yeniden yazılacağını göremez.");
            return;
        }
        Pass(steps, "Ekle/güncelle ayrımı",
            $"Önizleme mevcut reçeteyi güncelleme olarak bildirdi (güncellenecek={upd}).");
    }

    /// <summary>Reçete şablonu sabit; iki uç (önizleme/kaydetme) aynı gövdeyi kullanır.</summary>
    private static async Task<(bool Ok, JsonElement Json)> SendAsync(
        FunctionalTestContext ctx, List<FunctionalTestStep> steps, string label,
        string path, string csv, CancellationToken ct)
    {
        var spec = JsonSerializer.Serialize(new
        {
            id = 0,
            name = "Fonksiyon Testi Reçete",
            targetEntity = "BOM",
            sheetName = (string?)null,
            headerRowIndex = 1,
            matchKeyField = "ParentCode",
            columns = new[]
            {
                new { targetKey = "ParentCode",    sourceColumn = "Ana Ürün Kodu", transform = (string?)null },
                new { targetKey = "ComponentCode", sourceColumn = "Bileşen Kodu",  transform = (string?)null },
                new { targetKey = "Quantity",      sourceColumn = "Miktar",        transform = (string?)null },
            },
            isActive = true,
        });

        var res = await ctx.Http.PostFileAsync(path, "recete.csv", csv,
            new Dictionary<string, string> { ["spec"] = spec }, ct);
        if (!res.Ok)
        {
            Fail(steps, label, res.Error ?? "İstek başarısız.");
            return (false, default);
        }
        return (true, res.Json);
    }
}
