using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Ondalık Ayarları — form bazlı hane hassasiyetinin hem ÇÖZÜMLENMESİNİ hem de gerçek bir
/// belge kaydında UYGULANMASINI sınar.
///
/// Çözümleme zinciri: form kaydı → '*' şirket varsayılanı → sabit fallback. Senaryo bu
/// zinciri iki yönlü gezer (form kaydı sil → varsayılana düş, kaydet → forma yüksel) ve
/// dönen <c>source</c> alanının doğru kaynağı bildirdiğini doğrular.
///
/// EN KRİTİK ADIM sonuncusudur: ayarın gerçekten yuvarlamaya etki ettiği, fazla ondalıklı
/// bir birim fiyatla belge kaydedilip geri okunarak kanıtlanır. Ayar ekranı "kaydedildi"
/// dese bile hesaplama tarafı onu okumuyorsa kullanıcı ekranda 2 hane görür, veride 6 hane
/// taşır — para tutarlarında sessiz ve kalıcı bir sapma.
///
/// Senaryo kendi izini TEMİZLER: sınama bitince form kaydı silinip varsayılana döndürülür,
/// aksi halde sonraki tüm satış senaryoları bu test ayarıyla koşardı.
/// </summary>
public sealed class DecimalSettingsScenario : FunctionalTestScenarioBase
{
    /// <summary>Sınama için kullanılan belge tipi — ticari zincirin de kullandığı form.</summary>
    private const string TargetForm = "SALES_QUOTE";

    /// <summary>Sınama sırasında uygulanacak hane sayıları (varsayılandan FARKLI seçildi).</summary>
    private const int TestQuantityDecimals = 3;
    private const int TestUnitPriceDecimals = 3;
    private const int TestAmountDecimals = 3;

    public override string Key => "DECIMAL_SETTINGS";
    public override string Group => "ticari";
    public override string Label => "Ondalık Ayarları (Hane Hassasiyeti)";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        // ── 1) Ayar ekranı listesi — '*' varsayılanı ve hedef form satırı var mı ──
        var (listOk, listJson) = await StepGetAsync(ctx, steps, "Ayar listesini okuma", "/Admin/DecimalSettings/ListJson", ct);
        if (!listOk) return;
        var rows = listJson.GetArrayCI("rows");
        var starRow = rows.FirstOrDefault(r => r.GetStringCI("formCode") == "*");
        if (starRow.ValueKind != JsonValueKind.Object)
        {
            Fail(steps, "Ayar listesini okuma", "Şirket geneli varsayılan ('*') satırı listede yok.");
            return;
        }
        if (!rows.Any(r => string.Equals(r.GetStringCI("formCode"), TargetForm, StringComparison.OrdinalIgnoreCase)))
        {
            Fail(steps, "Ayar listesini okuma", $"'{TargetForm}' formu ayar listesinde yok — form kaydı eksik olabilir.");
            return;
        }
        Pass(steps, "Ayar listesini okuma", $"{rows.Count} satır · '*' varsayılanı mevcut.");

        // ── 2) Form kaydı yokken varsayılandan düşülüyor mu ──
        await ctx.Http.PostAsync("/Admin/DecimalSettings/ResetJson", new { formCode = TargetForm }, ct);
        var (baseOk, baseJson) = await StepGetAsync(ctx, steps, "Form kaydı yokken çözümleme",
            $"/Decimals/Effective?formCode={TargetForm}", ct);
        if (!baseOk) return;
        var baseSource = baseJson.GetStringCI("source");
        if (string.Equals(baseSource, "form", StringComparison.OrdinalIgnoreCase))
        {
            Fail(steps, "Form kaydı yokken çözümleme",
                "Form kaydı silindiği hâlde ayar hâlâ 'form' kaynağından geliyor — sıfırlama etkisiz.");
            return;
        }
        Pass(steps, "Form kaydı yokken çözümleme", $"Kaynak: {baseSource} (varsayılan/fallback).");

        // ── 3) Forma özel kayıt → çözümleme forma yükselmeli ──
        var (saveOk, _) = await StepPostAsync(ctx, steps, "Forma özel ayar kaydetme", "/Admin/DecimalSettings/SaveJson",
            new
            {
                formCode = TargetForm,
                quantityDecimals = TestQuantityDecimals,
                unitPriceDecimals = TestUnitPriceDecimals,
                fxUnitPriceDecimals = 4,
                amountDecimals = TestAmountDecimals,
                rateDecimals = 2,
                exchangeRateDecimals = 4,
            }, ct);
        if (!saveOk) return;

        var (effOk, effJson) = await StepGetAsync(ctx, steps, "Forma özel ayarın çözümlenmesi",
            $"/Decimals/Effective?formCode={TargetForm}", ct);
        if (!effOk) return;
        var source = effJson.GetStringCI("source");
        var qty = effJson.GetIntCI("quantity");
        var price = effJson.GetIntCI("unitPrice");
        var amount = effJson.GetIntCI("amount");
        if (!string.Equals(source, "form", StringComparison.OrdinalIgnoreCase)
            || qty != TestQuantityDecimals || price != TestUnitPriceDecimals || amount != TestAmountDecimals)
        {
            Fail(steps, "Forma özel ayarın çözümlenmesi",
                $"Beklenen kaynak 'form' ve {TestQuantityDecimals}/{TestUnitPriceDecimals}/{TestAmountDecimals}; " +
                $"gerçek '{source}' ve {qty}/{price}/{amount}.");
            await ResetAsync(ctx, ct);
            return;
        }
        Pass(steps, "Forma özel ayarın çözümlenmesi", $"Kaynak: form · miktar={qty}, fiyat={price}, tutar={amount}.");

        // ── 4) Gerçek etki: fazla ondalıklı fiyat kaydedilip geri okunur ──
        var applied = await VerifyRoundingAsync(ctx, steps, ct);

        // İz temizliği — sonraki senaryolar bu test ayarıyla koşmasın.
        await ResetAsync(ctx, ct);
        if (!applied) return;

        var (finalOk, finalJson) = await StepGetAsync(ctx, steps, "Ayarın varsayılana döndürülmesi",
            $"/Decimals/Effective?formCode={TargetForm}", ct);
        if (!finalOk) return;
        if (string.Equals(finalJson.GetStringCI("source"), "form", StringComparison.OrdinalIgnoreCase))
        {
            Fail(steps, "Ayarın varsayılana döndürülmesi", "Test ayarı temizlenemedi — form kaydı hâlâ duruyor.");
            return;
        }
        Pass(steps, "Ayarın varsayılana döndürülmesi", "Form kaydı silindi, varsayılana dönüldü.");
    }

    /// <summary>
    /// Ayarın hesaplamaya etkisi: birim fiyat ayardan DAHA ÇOK ondalıkla gönderilir; kaydedilen
    /// değerin ayarın izin verdiği haneye yuvarlanmış olması beklenir.
    /// </summary>
    private static async Task<bool> VerifyRoundingAsync(
        FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var item1Id = ctx.GetInt(FunctionalTestContext.Keys.Item1Id);
        var unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);
        var contactId = ctx.GetInt(FunctionalTestContext.Keys.ContactId);
        var currencyId = ctx.GetInt(FunctionalTestContext.Keys.CurrencyId);

        var typeId = await ctx.ResolveDocumentTypeIdAsync("satis_teklifi", ct);
        if (typeId is not > 0)
        {
            Fail(steps, "Yuvarlama doğrulama", "'satis_teklifi' belge tipi bulunamadı.");
            return false;
        }

        // 12.3456789 → 3 haneye yuvarlanınca 12.346 olmalı (12.3456789 DEĞİL).
        const decimal rawPrice = 12.3456789m;
        const decimal expected = 12.346m;

        var (saveOk, saveJson) = await StepPostAsync(ctx, steps, "Fazla ondalıklı fiyatla belge kaydetme", "/Sales/SaveDocument",
            new
            {
                id = (int?)null, documentDate = DateTime.Today, validUntil = (DateTime?)null,
                contactId, contactName = (string?)null, contactAddress = (string?)null, salesRepId = (int?)null,
                currencyId, discountRate = 0m, taxRate = 20m,
                paymentTerms = (string?)null, deliveryTerms = (string?)null, deliveryAddress = (string?)null,
                notes = "Fonksiyon testi — ondalık yuvarlama",
                lines = new object[]
                {
                    new { id = (int?)null, itemId = item1Id, unitId, quantity = 1m, unitPrice = rawPrice, discountRate = 0m, combinationId = (int?)null, locationId = loc1, notes = (string?)null },
                },
                contactCode = (string?)null, documentTypeId = typeId, deliveryDate = (DateTime?)null, deliveryDays = (int?)null,
                requesterPersonnelId = (int?)null, fromRequestId = (int?)null, locationId = loc1,
                exchangeRate = 1m, isVatIncluded = false, rateDate = (DateTime?)null, sourceDocumentNo = (string?)null,
            }, ct);
        if (!saveOk) return false;

        var quote = saveJson.TryGetPropertyCI("quote", out var q) ? q : default;
        var docId = quote.ValueKind == JsonValueKind.Object ? quote.GetIntCI("id") : 0;
        if (docId <= 0) { Fail(steps, "Fazla ondalıklı fiyatla belge kaydetme", "Sunucu belge Id'si döndürmedi."); return false; }

        var (readOk, readJson) = await StepGetAsync(ctx, steps, "Kaydedilen fiyatı geri okuma", $"/Sales/GetQuote?id={docId}", ct);
        if (!readOk) return false;
        var line = readJson.GetArrayCI("lines").FirstOrDefault();
        if (line.ValueKind != JsonValueKind.Object)
        {
            Fail(steps, "Kaydedilen fiyatı geri okuma", "Belgede kalem bulunamadı.");
            return false;
        }
        var savedPrice = line.GetDecimalCI("unitPrice");

        if (Math.Abs(savedPrice - expected) < 0.00001m)
        {
            Pass(steps, "Ondalık yuvarlama uygulanıyor", $"{rawPrice} → {savedPrice} ({TestUnitPriceDecimals} hane).");
            return true;
        }
        if (Math.Abs(savedPrice - rawPrice) < 0.00001m)
        {
            Fail(steps, "Ondalık yuvarlama uygulanıyor",
                $"Fiyat hiç yuvarlanmadı ({savedPrice}) — ayar {TestUnitPriceDecimals} hane diyor ama hesaplama tarafı okumuyor. " +
                "Ekranda görünen ile veride duran değer birbirini tutmaz.");
            return false;
        }
        Fail(steps, "Ondalık yuvarlama uygulanıyor",
            $"Beklenen {expected}, kaydedilen {savedPrice} (gönderilen {rawPrice}).");
        return false;
    }

    private static async Task ResetAsync(FunctionalTestContext ctx, CancellationToken ct)
        => await ctx.Http.PostAsync("/Admin/DecimalSettings/ResetJson", new { formCode = TargetForm }, ct);
}
