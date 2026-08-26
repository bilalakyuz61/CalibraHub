using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Belge aktarımında KONFİGÜRASYON EŞLEŞMESİ — kombinasyon kodu bilinmeden, özellik=değer
/// tarifiyle doğru varyantın bulunmasını sınar (DocumentImportHandler kalem kurma yolu).
///
/// Neden gerekli: kombinasyon kodu sunucuda otomatik üretilir; dış sistemden Excel hazırlayan
/// kullanıcı onu bilemez. Bu yüzden satırda "Renk=Kırmızı; Beden=S" yazılabiliyor. Ama bu
/// kolaylığın iki tehlikeli kenarı var ve ikisi de SESSİZ:
///   · tarif birden çok kombinasyona uyuyorsa (eksik özellik) ilki seçilirse siparişe YANLIŞ
///     varyant yazılır ve belge "başarıyla aktarıldı" göründüğü için kimse fark etmez;
///   · kod ve tarif birlikte verilip ÇELİŞİRSE, koda sessizce uyulması aynı sonucu doğurur.
///
/// Doğrulama noktası bilinçli olarak commit yanıtı DEĞİL: "aktarım başarılı" yanlış varyant
/// yazıldığında da döner. Bu yüzden belge geri okunup KALEMİN kombinasyonu karşılaştırılır.
///
/// Zemin COMB_GENERATE'ten gelir (2 özellik × 2 değer = 4 kombinasyon); tarif metni de o
/// kombinasyonun KENDİ hücrelerinden üretilir — sabit metin yazılsaydı isimlendirme
/// değiştiğinde test sessizce anlamsızlaşırdı.
/// </summary>
public sealed class ImportDocumentConfigScenario : FunctionalTestScenarioBase
{
    private const string Target = "DOC_SALES_ORDER";

    public override string Key => "IMPORT_DOC_CONFIG";
    public override string Group => "ticari";
    public override string Label => "Belge Aktarımı: Konfigürasyon Eşleşmesi";
    public override IReadOnlyList<string> DependsOn => new[] { "COMB_GENERATE" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var itemCode = ctx.GetString(FunctionalTestContext.Keys.ComboItemCode);
        if (string.IsNullOrWhiteSpace(itemCode))
        {
            Fail(steps, "Kombinasyonlu ürün", "Bağlamda kombinasyonlu stok kodu yok.");
            return;
        }

        // ── Zemin: üretilmiş kombinasyonları oku ────────────────────────────
        var (cOk, cJson) = await StepGetAsync(ctx, steps, "Kombinasyonları okuma",
            $"/Logistics/CombinationsDataJson?stockCode={Uri.EscapeDataString(itemCode)}", ct);
        if (!cOk) return;

        var combos = cJson.GetArrayCI("combos").ToList();
        if (combos.Count < 2)
        {
            Fail(steps, "Kombinasyonları okuma",
                $"En az 2 kombinasyon gerekli (belirsizlik sınanacak), {combos.Count} bulundu.");
            return;
        }

        var target = combos[0];
        var other = combos[1];
        var targetId = target.GetIntCI("id");
        var targetCode = target.GetStringCI("code");
        var cells = target.GetArrayCI("cells").ToList();
        if (targetId <= 0 || cells.Count < 2)
        {
            Fail(steps, "Kombinasyonları okuma", "Hedef kombinasyon iki özellikli değil — belirsizlik sınanamaz.");
            return;
        }

        // "Özellik Adı=Değer Kodu" çiftleri — çözümleyici özellik ADI ve değer KODU ile eşleşir.
        string Pair(JsonElement cell) => cell.GetStringCI("featureName") + "=" + cell.GetStringCI("valueCode");
        var fullConfig = string.Join("; ", cells.Select(Pair));
        var partialConfig = Pair(cells[0]);          // tek özellik → 2 kombinasyona uyar

        // ── Aktarım için cari ───────────────────────────────────────────────
        var sfx = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var contactCode = $"FNKONF-{sfx}";
        var (ctOk, _) = await StepPostAsync(ctx, steps, "Cari oluşturma", "/Finance/UpsertContact",
            new
            {
                id = (int?)null, accountType = (byte)1, accountCode = contactCode,
                accountTitle = $"Konfigürasyon Testi Cari {sfx}", taxNumber = (string?)null,
                identityNumber = (string?)null, taxOffice = (string?)null, phone = (string?)null,
                email = (string?)null, address = (string?)null, city = (string?)null, district = (string?)null,
                isActive = true, priceGroupId = (int?)null,
            }, ct);
        if (!ctOk) return;

        var today = DateTime.Today.ToString("dd.MM.yyyy");

        // ── 1) Tam tarif → DOĞRU kombinasyon yazılmalı ──────────────────────
        var okDocNo = $"KONF-OK-{sfx}";
        var (i1Ok, i1) = await CommitAsync(ctx, steps, "Tam tarifle aktarım",
            Header() + $"{okDocNo};{today};{contactCode};{itemCode};3;{fullConfig};\n", ct);
        if (!i1Ok) return;
        if (i1.GetIntCI("inserted") != 1)
        {
            Fail(steps, "Tam tarifle aktarım",
                $"Belge oluşmadı (eklendi={i1.GetIntCI("inserted")} hata={i1.GetIntCI("failed")}). " +
                $"Tarif: '{fullConfig}'");
            return;
        }

        var docId = i1.GetArrayCI("rows").Select(r => r.GetIntCI("recordId")).FirstOrDefault(x => x > 0);
        if (docId <= 0) { Fail(steps, "Tam tarifle aktarım", "Oluşan belgenin Id'si dönmedi."); return; }
        Pass(steps, "Tam tarifle aktarım", $"Belge oluştu (#{docId}).");

        // ASIL DOĞRULAMA: satırdaki kombinasyon, tarif edilen kombinasyon mu?
        var (rOk, rJson) = await StepGetAsync(ctx, steps, "Belgeyi geri okuma", $"/Sales/GetQuote?id={docId}", ct);
        if (!rOk) return;

        var savedCombo = FindCombinationId(rJson);
        if (savedCombo != targetId)
        {
            Fail(steps, "Doğru varyant kontrolü",
                $"Tarif '{fullConfig}' → #{targetId} bekleniyordu, kalemde #{savedCombo} var. " +
                "Aktarım başarılı görünürken siparişe BAŞKA varyant yazılmış.");
            return;
        }
        Pass(steps, "Doğru varyant kontrolü", $"Kalem doğru kombinasyona bağlandı (#{targetId}, {targetCode}).");

        // ── 2) Eksik tarif → BELİRSİZ, reddedilmeli ─────────────────────────
        var (i2Ok, i2) = await CommitAsync(ctx, steps, "Eksik tarifle aktarım",
            Header() + $"KONF-AMB-{sfx};{today};{contactCode};{itemCode};1;{partialConfig};\n", ct);
        if (!i2Ok) return;
        if (i2.GetIntCI("inserted") > 0)
        {
            Fail(steps, "Belirsizlik reddi",
                $"'{partialConfig}' birden çok kombinasyona uyuyor ama satır KABUL edildi — " +
                "sisteme rastgele bir varyant yazılmış olabilir.");
            return;
        }
        Pass(steps, "Belirsizlik reddi", "Eksik tarif reddedildi; rastgele varyant seçilmedi.");

        // ── 3) Kod ile tarif çelişirse → reddedilmeli ───────────────────────
        // Hedefin KODU + BAŞKA kombinasyonun tarifi. İkisi çelişir.
        var otherCells = other.GetArrayCI("cells").ToList();
        var conflictPair = otherCells
            .Select(Pair)
            .FirstOrDefault(p => !cells.Select(Pair).Contains(p, StringComparer.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(conflictPair) || string.IsNullOrWhiteSpace(targetCode))
        {
            Pass(steps, "Çelişki reddi",
                "İki kombinasyon arasında çelişecek çift bulunamadı — bu ortamda sınanamıyor.");
            return;
        }

        var (i3Ok, i3) = await CommitAsync(ctx, steps, "Kod + çelişen tarif",
            Header() + $"KONF-CNF-{sfx};{today};{contactCode};{itemCode};1;{conflictPair};{targetCode}\n", ct);
        if (!i3Ok) return;
        if (i3.GetIntCI("inserted") > 0)
        {
            Fail(steps, "Çelişki reddi",
                $"Kombinasyon kodu '{targetCode}' ile tarif '{conflictPair}' çelişiyor ama satır KABUL edildi.");
            return;
        }
        Pass(steps, "Çelişki reddi", "Kod ile tarif çeliştiğinde satır reddedildi.");
    }

    private static string Header()
        => "Kaynak Belge No;Belge Tarihi;Cari Kodu;Malzeme Kodu;Miktar;Konfigürasyon;Kombinasyon\n";

    /// <summary>
    /// Belge yanıtındaki ilk kalemin kombinasyon Id'si. Yanıt biçimi sürümle değişebildiği
    /// için alan adı ham JSON üzerinde aranır — sabit bir yol varsayımı testi kırılgan yapardı.
    /// </summary>
    private static int FindCombinationId(JsonElement doc)
    {
        foreach (var line in doc.GetArrayCI("lines"))
        {
            var v = line.GetIntCI("combinationId");
            if (v > 0) return v;
            if (line.TryGetPropertyCI("configId", out var c) && c.ValueKind == JsonValueKind.Number)
                return c.GetInt32();
        }
        return 0;
    }

    private static async Task<(bool Ok, JsonElement Json)> CommitAsync(
        FunctionalTestContext ctx, List<FunctionalTestStep> steps, string label, string csv, CancellationToken ct)
    {
        var spec = JsonSerializer.Serialize(new
        {
            id = 0,
            name = "Fonksiyon Testi Konfigürasyon",
            targetEntity = Target,
            sheetName = (string?)null,
            headerRowIndex = 1,
            matchKeyField = "SourceDocumentNo",
            columns = new[]
            {
                new { targetKey = "SourceDocumentNo", sourceColumn = "Kaynak Belge No", transform = (string?)null },
                new { targetKey = "DocumentDate",     sourceColumn = "Belge Tarihi",    transform = (string?)null },
                new { targetKey = "ContactCode",      sourceColumn = "Cari Kodu",       transform = (string?)null },
                new { targetKey = "ItemCode",         sourceColumn = "Malzeme Kodu",    transform = (string?)null },
                new { targetKey = "Quantity",         sourceColumn = "Miktar",          transform = (string?)null },
                new { targetKey = "Configuration",    sourceColumn = "Konfigürasyon",   transform = (string?)null },
                new { targetKey = "Combination",      sourceColumn = "Kombinasyon",     transform = (string?)null },
            },
            isActive = true,
        });

        var res = await ctx.Http.PostFileAsync("/Import/api/commit", "siparis.csv", csv,
            new Dictionary<string, string> { ["spec"] = spec }, ct);
        if (!res.Ok)
        {
            Fail(steps, label, res.Error ?? "İçe aktarım isteği başarısız.");
            return (false, default);
        }
        return (true, res.Json);
    }
}
