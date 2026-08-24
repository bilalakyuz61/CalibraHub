using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Kit (Paket Ürün) Tanımlama — kit kartı (ItemType.Kit) + iki bileşen kartı açar,
/// bileşenleri Depo 1'e sokar ve kit içeriğini kaydeder.
///
/// Kit fiziksel stok DEĞİLDİR (phantom bundle): satış/teklif/siparişte tek kalem görünür,
/// irsaliyede bileşenlerine patlar. Bu yüzden stoğa kitin kendisi değil BİLEŞENLERİ girer —
/// teslimat senaryosu düşüşü bileşen bakiyesinde arar.
///
/// Bileşenler bilinçli olarak kite özeldir: ticari zincirin item1/item2 bakiyesi satış
/// teslimatlarıyla tüketiliyor, paylaşılsaydı senaryolar sıraya göre rastgele kırılırdı.
/// </summary>
public sealed class KitDefineScenario : FunctionalTestScenarioBase
{
    public const decimal Comp1PerSet = 2m;
    public const decimal Comp2PerSet = 1m;
    public const decimal ComponentStock = 200m;
    public const decimal FixedPrice = 500m;

    /// <summary>Kit bileşenlerinin Id'leri — sipariş/teslimat senaryoları bakiye kontrolünde kullanır.</summary>
    public const string KitComp1IdKey = "kitComp1Id";
    public const string KitComp2IdKey = "kitComp2Id";

    public override string Key => "KIT_DEFINE";
    public override string Group => "ticari";
    public override string Label => "Kit (Paket Ürün) Tanımlama";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var (c1Ok, comp1Id) = await SaveCardAsync(ctx, steps, "Kit bileşeni 1 kartı oluşturma",
            $"FNKC1-{suffix}", $"Fonksiyon Test Kit Bileşen 1 ({suffix})", typeId: 8, unitId, ct);
        if (!c1Ok) return;
        ctx.Set(KitComp1IdKey, comp1Id);

        var (c2Ok, comp2Id) = await SaveCardAsync(ctx, steps, "Kit bileşeni 2 kartı oluşturma",
            $"FNKC2-{suffix}", $"Fonksiyon Test Kit Bileşen 2 ({suffix})", typeId: 8, unitId, ct);
        if (!c2Ok) return;
        ctx.Set(KitComp2IdKey, comp2Id);

        // typeId 10 = ItemType.Kit — belge ekranı kalemi "KİT" olarak bu tipten tanır.
        var kitCode = $"FNKIT-{suffix}";
        var (kitCardOk, kitItemId) = await SaveCardAsync(ctx, steps, "Kit kartı oluşturma",
            kitCode, $"Fonksiyon Test Kit ({suffix})", typeId: 10, unitId, ct);
        if (!kitCardOk) return;
        ctx.Set(FunctionalTestContext.Keys.KitItemId, kitItemId);
        ctx.Set(FunctionalTestContext.Keys.KitItemCode, kitCode);

        var (stockOk, _) = await StepPostAsync(ctx, steps, "Kit bileşenleri stok girişi", "/Warehouse/SaveDocJson",
            new
            {
                id = (int?)null, docType = "STOCK_IN", docNo = (string?)null, docDate = DateTime.Today,
                fromLocationId = (int?)null, toLocationId = loc1, refNo = (string?)null,
                notes = "Fonksiyon testi — kit bileşen girişi",
                lines = new object[]
                {
                    new { id = (int?)null, itemId = comp1Id, unitId, qty = ComponentStock, combinationId = (int?)null, notes = (string?)null, fromLocationId = (int?)null, toLocationId = loc1, unitCost = (decimal?)null },
                    new { id = (int?)null, itemId = comp2Id, unitId, qty = ComponentStock, combinationId = (int?)null, notes = (string?)null, fromLocationId = (int?)null, toLocationId = loc1, unitCost = (decimal?)null },
                },
                argeProjectId = (int?)null,
            }, ct);
        if (!stockOk) return;

        var (kitOk, _) = await StepPostAsync(ctx, steps, "Kit içeriğini kaydetme", "/Logistics/SaveKit",
            new
            {
                id = (int?)null, itemId = kitItemId, materialCode = (string?)null,
                priceMode = "FixedPackage", fixedPrice = FixedPrice,
                description = "Fonksiyon testi — kit içeriği",
                lines = new object[]
                {
                    new { itemId = comp1Id, configId = (int?)null, componentMaterialCode = (string?)null, componentConfigCode = (string?)null, quantity = Comp1PerSet, note = (string?)null, unitPrice = (decimal?)null, unitId = (int?)unitId },
                    new { itemId = comp2Id, configId = (int?)null, componentMaterialCode = (string?)null, componentConfigCode = (string?)null, quantity = Comp2PerSet, note = (string?)null, unitPrice = (decimal?)null, unitId = (int?)unitId },
                },
            }, ct);
        if (!kitOk) return;

        var (readOk, readJson) = await StepGetAsync(ctx, steps, "Kit içeriğini geri okuma", $"/Logistics/GetKit?itemId={kitItemId}", ct);
        if (!readOk) return;
        if (readJson.GetBoolCI("found") == false)
        {
            Fail(steps, "Kit içeriğini geri okuma", "Kit kaydı bulunamadı (found=false).");
            return;
        }
        var lines = readJson.GetArrayCI("lines");
        var q1 = lines.Where(l => l.GetIntCI("itemId") == comp1Id).Select(l => l.GetDecimalCI("quantity")).DefaultIfEmpty(0m).Sum();
        var q2 = lines.Where(l => l.GetIntCI("itemId") == comp2Id).Select(l => l.GetDecimalCI("quantity")).DefaultIfEmpty(0m).Sum();
        if (lines.Count != 2 || Math.Abs(q1 - Comp1PerSet) > 0.0001m || Math.Abs(q2 - Comp2PerSet) > 0.0001m)
        {
            Fail(steps, "Kit içerik doğrulama",
                $"Satır sayısı={lines.Count} (2), bileşen miktarları {q1}/{q2} (beklenen {Comp1PerSet}/{Comp2PerSet}).");
            return;
        }
        Pass(steps, "Kit içerik doğrulama", $"Kit #{kitItemId}: bileşen1={q1}, bileşen2={q2} (set başına).");
    }

    private static async Task<(bool Ok, int Id)> SaveCardAsync(
        FunctionalTestContext ctx, List<FunctionalTestStep> steps, string label,
        string code, string name, int typeId, int unitId, CancellationToken ct)
    {
        var (ok, json) = await StepPostAsync(ctx, steps, label, "/Logistics/SaveMaterialCardJson",
            new
            {
                itemId = (int?)null, code, name, typeId, unitId, combinations = false,
                taxRate = 20m, trackingType = "None", minStock = 0m, autoSerial = false,
            }, ct);
        if (!ok) return (false, 0);
        var id = json.GetIntCI("id");
        if (id <= 0) { Fail(steps, label, "Sunucu Id döndürmedi."); return (false, 0); }
        return (true, id);
    }
}

/// <summary>
/// Kit ile Sipariş Oluşturma — kit kalemli bir satış siparişi kaydeder ve siparişin açık
/// kalem okumasında satırın KİT olarak tanındığını + bileşen dökümünün (set başına oran)
/// donmuş snapshot'tan geldiğini doğrular. Snapshot yoksa teslimat bileşenlere patlayamaz,
/// bu yüzden "HTTP 200" yetmez — kitComponents içeriği aranır.
/// </summary>
public sealed class KitOrderCreateScenario : FunctionalTestScenarioBase
{
    public const decimal OrderSets = 3m;

    public override string Key => "KIT_ORDER_CREATE";
    public override string Group => "ticari";
    public override string Label => "Kit ile Sipariş Oluşturma";
    public override IReadOnlyList<string> DependsOn => new[] { "KIT_DEFINE" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var kitItemId = ctx.GetInt(FunctionalTestContext.Keys.KitItemId);
        var kitItemCode = ctx.GetString(FunctionalTestContext.Keys.KitItemCode);
        var unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);
        var contactId = ctx.GetInt(FunctionalTestContext.Keys.ContactId);
        var currencyId = ctx.GetInt(FunctionalTestContext.Keys.CurrencyId);

        var typeId = await ctx.ResolveDocumentTypeIdAsync("satis_siparisi", ct);
        if (typeId is not > 0) { Fail(steps, "Belge tipi çözümleme (satis_siparisi)", "'satis_siparisi' DocumentType kaydı bulunamadı."); return; }

        var (saveOk, saveJson) = await StepPostAsync(ctx, steps, "Kit kalemli sipariş oluşturma", "/Sales/SaveDocument",
            new
            {
                id = (int?)null, documentDate = DateTime.Today, validUntil = (DateTime?)null,
                contactId, contactName = (string?)null, contactAddress = (string?)null, salesRepId = (int?)null,
                currencyId, discountRate = 0m, taxRate = 20m,
                paymentTerms = (string?)null, deliveryTerms = (string?)null, deliveryAddress = (string?)null,
                notes = "Fonksiyon testi — kit siparişi",
                lines = new object[]
                {
                    new { id = (int?)null, itemId = kitItemId, unitId, quantity = OrderSets, unitPrice = KitDefineScenario.FixedPrice, discountRate = 0m, combinationId = (int?)null, locationId = loc1, notes = (string?)null },
                },
                contactCode = (string?)null, documentTypeId = typeId, deliveryDate = (DateTime?)null, deliveryDays = (int?)null,
                requesterPersonnelId = (int?)null, fromRequestId = (int?)null, locationId = loc1,
                exchangeRate = 1m, isVatIncluded = false, rateDate = (DateTime?)null, sourceDocumentNo = (string?)null,
            }, ct);
        if (!saveOk) return;
        var quote = saveJson.TryGetPropertyCI("quote", out var q) ? q : default;
        var orderId = quote.ValueKind == JsonValueKind.Object ? quote.GetIntCI("id") : 0;
        if (orderId <= 0) { Fail(steps, "Kit kalemli sipariş oluşturma", "Sunucu belge Id'si döndürmedi."); return; }
        ctx.Set(FunctionalTestContext.Keys.KitOrderDocId, orderId);

        var (openOk, openJson) = await StepGetAsync(ctx, steps, "Açık sipariş kalemini okuma",
            $"/Warehouse/OrderOpenLinesJson?orderId={orderId}", ct);
        if (!openOk) return;
        var line = openJson.GetArrayCI("lines")
            .FirstOrDefault(l => string.Equals(l.GetStringCI("itemCode"), kitItemCode, StringComparison.OrdinalIgnoreCase));
        if (line.ValueKind != JsonValueKind.Object)
        {
            Fail(steps, "Açık sipariş kalemini okuma", $"'{kitItemCode}' kalemi açık listede bulunamadı.");
            return;
        }
        if (line.GetBoolCI("isKit") != true)
        {
            Fail(steps, "Kit satırı doğrulama", "Satır KİT olarak tanınmadı (isKit=false) — teslimatta bileşenlere patlamaz.");
            return;
        }
        var comps = line.GetArrayCI("kitComponents");
        if (comps.Count != 2)
        {
            Fail(steps, "Kit satırı doğrulama", $"Kit bileşen dökümü {comps.Count} satır (2 bekleniyordu).");
            return;
        }
        var perSetTotal = comps.Select(c => c.GetDecimalCI("perSet")).Sum();
        var expectedPerSet = KitDefineScenario.Comp1PerSet + KitDefineScenario.Comp2PerSet;
        if (Math.Abs(perSetTotal - expectedPerSet) > 0.0001m)
        {
            Fail(steps, "Kit satırı doğrulama", $"Set başına toplam oran {perSetTotal} (beklenen {expectedPerSet}).");
            return;
        }
        Pass(steps, "Kit satırı doğrulama", $"Sipariş #{orderId}: {OrderSets} set, 2 bileşen (set başına {perSetTotal}).");
    }
}

/// <summary>
/// Kit Siparişi Teslimatı — kit satırının tüm setleri irsaliyeye çevrilir. Kit fiziksel stok
/// olmadığı için doğrulama BİLEŞEN bakiyelerinde yapılır: her bileşen (set başına oran × set
/// sayısı) kadar düşmeli. Ayrıca sipariş satırının açık miktarı sıfırlanmalı.
/// </summary>
public sealed class KitDeliveryScenario : FunctionalTestScenarioBase
{
    public override string Key => "KIT_DELIVERY";
    public override string Group => "ticari";
    public override string Label => "Kit Siparişi Teslimatı (Bileşene Patlatma)";
    public override IReadOnlyList<string> DependsOn => new[] { "KIT_ORDER_CREATE" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var orderId = ctx.GetInt(FunctionalTestContext.Keys.KitOrderDocId);
        var kitItemCode = ctx.GetString(FunctionalTestContext.Keys.KitItemCode);
        var comp1Id = ctx.GetInt(KitDefineScenario.KitComp1IdKey);
        var comp2Id = ctx.GetInt(KitDefineScenario.KitComp2IdKey);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);

        var (openOk, openJson) = await StepGetAsync(ctx, steps, "Açık kit satırını okuma",
            $"/Warehouse/OrderOpenLinesJson?orderId={orderId}", ct);
        if (!openOk) return;
        var line = openJson.GetArrayCI("lines")
            .FirstOrDefault(l => string.Equals(l.GetStringCI("itemCode"), kitItemCode, StringComparison.OrdinalIgnoreCase));
        var lineId = line.ValueKind == JsonValueKind.Object ? line.GetIntCI("lineId") : 0;
        var openSets = line.ValueKind == JsonValueKind.Object ? line.GetDecimalCI("open") : 0m;
        if (lineId <= 0 || openSets <= 0)
        {
            Fail(steps, "Açık kit satırını okuma", $"Teslim edilebilir kit satırı yok (lineId={lineId}, açık={openSets}).");
            return;
        }

        var before = await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { comp1Id, comp2Id }, ct);
        var before1 = FunctionalTestHelpers.BalanceFor(before, comp1Id, loc1);
        var before2 = FunctionalTestHelpers.BalanceFor(before, comp2Id, loc1);

        var (delOk, delJson) = await StepPostAsync(ctx, steps, "Kit teslimatı (İrsaliye oluşturma)", "/Warehouse/DeliverSalesOrderJson",
            new { orderId, lines = new object[] { new { lineId, quantity = openSets } } }, ct);
        if (!delOk) return;
        var docNo = delJson.GetStringCI("docNo");
        if (string.IsNullOrWhiteSpace(docNo)) { Fail(steps, "Kit teslimatı (İrsaliye oluşturma)", "Sunucu docNo döndürmedi."); return; }

        var (reopenOk, reopenJson) = await StepGetAsync(ctx, steps, "Kalan set doğrulama",
            $"/Warehouse/OrderOpenLinesJson?orderId={orderId}", ct);
        if (!reopenOk) return;
        var stillOpen = reopenJson.GetArrayCI("lines").FirstOrDefault(l => l.GetIntCI("lineId") == lineId);
        var remaining = stillOpen.ValueKind == JsonValueKind.Object ? stillOpen.GetDecimalCI("open") : 0m;
        if (remaining > 0.0001m)
        {
            Fail(steps, "Kalan set doğrulama", $"Teslimat sonrası kalan {remaining} set (0 bekleniyordu).");
            return;
        }
        Pass(steps, "Kalan set doğrulama", $"İrsaliye {docNo}, teslim={openSets} set.");

        var after = await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { comp1Id, comp2Id }, ct);
        var after1 = FunctionalTestHelpers.BalanceFor(after, comp1Id, loc1);
        var after2 = FunctionalTestHelpers.BalanceFor(after, comp2Id, loc1);
        var expected1 = before1 - openSets * KitDefineScenario.Comp1PerSet;
        var expected2 = before2 - openSets * KitDefineScenario.Comp2PerSet;
        if (Math.Abs(after1 - expected1) > 0.0001m || Math.Abs(after2 - expected2) > 0.0001m)
        {
            Fail(steps, "Bileşen bakiye doğrulama",
                $"Beklenen {expected1}/{expected2}, gerçek {after1}/{after2} (önce {before1}/{before2}). Kit bileşenlerine patlamamış olabilir.");
            return;
        }
        Pass(steps, "Bileşen bakiye doğrulama", $"bileşen1 {before1}→{after1}, bileşen2 {before2}→{after2}.");
    }
}
