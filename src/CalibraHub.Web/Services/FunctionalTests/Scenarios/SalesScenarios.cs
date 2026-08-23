using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>Satış Teklifi Oluşturma — 2 kalemli bir satış teklifi kaydeder (SALES_ORDER_FROM_QUOTE + iki teslimat senaryosu bu belgeyi kullanır).</summary>
public sealed class SalesQuoteCreateScenario : FunctionalTestScenarioBase
{
    public const decimal Item1Qty = 20m;
    public const decimal Item2Qty = 10m;

    public override string Key => "SALES_QUOTE_CREATE";
    public override string Group => "ticari";
    public override string Label => "Satış Teklifi Oluşturma";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var item1Id = ctx.GetInt(FunctionalTestContext.Keys.Item1Id);
        var item2Id = ctx.GetInt(FunctionalTestContext.Keys.Item2Id);
        var unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);
        var contactId = ctx.GetInt(FunctionalTestContext.Keys.ContactId);
        var currencyId = ctx.GetInt(FunctionalTestContext.Keys.CurrencyId);

        var typeId = await ctx.ResolveDocumentTypeIdAsync("satis_teklifi", ct);
        if (typeId is not > 0) { Fail(steps, "Belge tipi çözümleme (satis_teklifi)", "'satis_teklifi' DocumentType kaydı bulunamadı."); return; }

        var (saveOk, saveJson) = await StepPostAsync(ctx, steps, "Satış teklifi oluşturma", "/Sales/SaveDocument",
            new
            {
                id = (int?)null, documentDate = DateTime.Today, validUntil = (DateTime?)null,
                contactId, contactName = (string?)null, contactAddress = (string?)null, salesRepId = (int?)null,
                currencyId, discountRate = 0m, taxRate = 20m,
                paymentTerms = (string?)null, deliveryTerms = (string?)null, deliveryAddress = (string?)null,
                notes = "Fonksiyon testi — Satış Teklifi",
                lines = new object[]
                {
                    new { id = (int?)null, itemId = item1Id, unitId, quantity = Item1Qty, unitPrice = 100m, discountRate = 0m, combinationId = (int?)null, locationId = loc1, notes = (string?)null },
                    new { id = (int?)null, itemId = item2Id, unitId, quantity = Item2Qty, unitPrice = 50m, discountRate = 0m, combinationId = (int?)null, locationId = loc1, notes = (string?)null },
                },
                contactCode = (string?)null, documentTypeId = typeId, deliveryDate = (DateTime?)null, deliveryDays = (int?)null,
                requesterPersonnelId = (int?)null, fromRequestId = (int?)null, locationId = loc1,
                exchangeRate = 1m, isVatIncluded = false, rateDate = (DateTime?)null, sourceDocumentNo = (string?)null,
            }, ct);
        if (!saveOk) return;

        var quote = saveJson.TryGetPropertyCI("quote", out var q) ? q : default;
        var docId = quote.ValueKind == JsonValueKind.Object ? quote.GetIntCI("id") : 0;
        var lineCount = saveJson.GetArrayCI("lines").Count;
        if (docId <= 0 || lineCount != 2)
        {
            Fail(steps, "Satış teklifi oluşturma", $"Belge Id={docId}, kalem sayısı={lineCount} (2 bekleniyordu).");
            return;
        }
        ctx.Set(FunctionalTestContext.Keys.SalesQuoteDocId, docId);
        Pass(steps, "Kalem doğrulama", $"Belge #{docId}, 2 kalem.");
    }
}

/// <summary>Satış Teklifi → Sipariş Dönüşümü — önce Onaylandı durumuna çekilir (CreateOrdersFromQuotesAsync yalnız Approved kabul eder), sonra ConvertSingleQuoteToOrder ile sipariş açılır.</summary>
public sealed class SalesOrderFromQuoteScenario : FunctionalTestScenarioBase
{
    public override string Key => "SALES_ORDER_FROM_QUOTE";
    public override string Group => "ticari";
    public override string Label => "Satış Teklifi → Sipariş Dönüşümü";
    public override IReadOnlyList<string> DependsOn => new[] { "SALES_QUOTE_CREATE" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var quoteDocId = ctx.GetInt(FunctionalTestContext.Keys.SalesQuoteDocId);

        var (approveOk, _) = await StepPostAsync(ctx, steps, "Teklifi onaylama", "/Sales/ChangeQuoteStatus",
            new { id = quoteDocId, status = "Approved" }, ct);
        if (!approveOk) return;

        var (convOk, convJson) = await StepPostAsync(ctx, steps, "Siparişe dönüştürme", "/Sales/ConvertSingleQuoteToOrder",
            new { quoteId = quoteDocId, orderDate = DateTime.Today, createWorkOrders = false }, ct);
        if (!convOk) return;
        var orderId = convJson.GetIntCI("orderId");
        if (orderId <= 0) { Fail(steps, "Siparişe dönüştürme", "Sunucu orderId döndürmedi."); return; }
        ctx.Set(FunctionalTestContext.Keys.SalesOrderDocId, orderId);

        var expectedTypeId = await ctx.ResolveDocumentTypeIdAsync("satis_siparisi", ct);
        var (checkOk, checkJson) = await StepGetAsync(ctx, steps, "Sipariş doğrulama", $"/Sales/GetQuote?id={orderId}", ct);
        if (!checkOk) return;
        var order = checkJson.TryGetPropertyCI("quote", out var oq) ? oq : default;
        var actualTypeId = order.GetIntOrNullCI("documentTypeId");
        var lineCount = checkJson.GetArrayCI("lines").Count;
        if (actualTypeId != expectedTypeId || lineCount != 2)
        {
            Fail(steps, "Sipariş doğrulama", $"Beklenen tip Id={expectedTypeId} kalem=2, gerçek tip Id={actualTypeId} kalem={lineCount}.");
            return;
        }
        Pass(steps, "Sipariş doğrulama", $"Belge #{orderId}.");
    }
}

/// <summary>Satış Siparişi → İrsaliye (TAM teslimat) — item1 satırının tüm açık miktarı teslim edilir.</summary>
public sealed class SalesDeliveryFullScenario : FunctionalTestScenarioBase
{
    public override string Key => "SALES_DELIVERY_FULL";
    public override string Group => "ticari";
    public override string Label => "Satış Siparişi → İrsaliye (Tam Teslimat)";
    public override IReadOnlyList<string> DependsOn => new[] { "SALES_ORDER_FROM_QUOTE", "STOCK_IN_RECEIPT" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var orderId = ctx.GetInt(FunctionalTestContext.Keys.SalesOrderDocId);
        var item1Code = ctx.GetString(FunctionalTestContext.Keys.Item1Code);
        var item1Id = ctx.GetInt(FunctionalTestContext.Keys.Item1Id);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);

        var (openOk, openJson) = await StepGetAsync(ctx, steps, "Açık sipariş kalemlerini okuma", $"/Warehouse/OrderOpenLinesJson?orderId={orderId}", ct);
        if (!openOk) return;
        var openLines = openJson.GetArrayCI("lines");
        var target = openLines.FirstOrDefault(l => string.Equals(l.GetStringCI("itemCode"), item1Code, StringComparison.OrdinalIgnoreCase));
        var lineId = target.ValueKind == JsonValueKind.Object ? target.GetIntCI("lineId") : 0;
        var openQty = target.ValueKind == JsonValueKind.Object ? target.GetDecimalCI("open") : 0m;
        if (lineId <= 0 || openQty <= 0)
        {
            Fail(steps, "Açık sipariş kalemlerini okuma", $"'{item1Code}' için açık kalem bulunamadı (lineId={lineId}, open={openQty}).");
            return;
        }

        var before = FunctionalTestHelpers.BalanceFor(await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { item1Id }, ct), item1Id, loc1);

        var (delOk, delJson) = await StepPostAsync(ctx, steps, "Tam teslimat (İrsaliye oluşturma)", "/Warehouse/DeliverSalesOrderJson",
            new { orderId, lines = new object[] { new { lineId, quantity = openQty } } }, ct);
        if (!delOk) return;
        var docNo = delJson.GetStringCI("docNo");
        if (string.IsNullOrWhiteSpace(docNo)) { Fail(steps, "Tam teslimat (İrsaliye oluşturma)", "Sunucu docNo döndürmedi."); return; }

        var (reopenOk, reopenJson) = await StepGetAsync(ctx, steps, "Kalan miktar doğrulama", $"/Warehouse/OrderOpenLinesJson?orderId={orderId}", ct);
        if (!reopenOk) return;
        var stillOpen = reopenJson.GetArrayCI("lines").FirstOrDefault(l => l.GetIntCI("lineId") == lineId);
        var remainingOpen = stillOpen.ValueKind == JsonValueKind.Object ? stillOpen.GetDecimalCI("open") : -1m;
        if (remainingOpen > 0.0001m)
        {
            Fail(steps, "Kalan miktar doğrulama", $"Tam teslimattan sonra kalan açık miktar {remainingOpen} (0 bekleniyordu).");
            return;
        }
        Pass(steps, "Kalan miktar doğrulama", $"Belge {docNo}, kalan={remainingOpen}.");

        var after = FunctionalTestHelpers.BalanceFor(await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { item1Id }, ct), item1Id, loc1);
        if (Math.Abs((before - openQty) - after) > 0.0001m)
        {
            Fail(steps, "Bakiye doğrulama", $"Depo 1 item1 bakiyesi {before}→{after}, beklenen {before - openQty}.");
            return;
        }
        Pass(steps, "Bakiye doğrulama", $"Depo 1 item1: {before} → {after}.");
    }
}

/// <summary>Satış Siparişi → İrsaliye (KISMİ teslimat) — item2 satırının yarısı teslim edilir, kalan açık miktarın sıfırdan büyük kaldığı doğrulanır.</summary>
public sealed class SalesDeliveryPartialScenario : FunctionalTestScenarioBase
{
    public override string Key => "SALES_DELIVERY_PARTIAL";
    public override string Group => "ticari";
    public override string Label => "Satış Siparişi → İrsaliye (Kısmi Teslimat)";
    public override IReadOnlyList<string> DependsOn => new[] { "SALES_ORDER_FROM_QUOTE", "STOCK_IN_RECEIPT" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var orderId = ctx.GetInt(FunctionalTestContext.Keys.SalesOrderDocId);
        var item2Code = ctx.GetString(FunctionalTestContext.Keys.Item2Code);
        var item2Id = ctx.GetInt(FunctionalTestContext.Keys.Item2Id);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);

        var (openOk, openJson) = await StepGetAsync(ctx, steps, "Açık sipariş kalemlerini okuma", $"/Warehouse/OrderOpenLinesJson?orderId={orderId}", ct);
        if (!openOk) return;
        var openLines = openJson.GetArrayCI("lines");
        var target = openLines.FirstOrDefault(l => string.Equals(l.GetStringCI("itemCode"), item2Code, StringComparison.OrdinalIgnoreCase));
        var lineId = target.ValueKind == JsonValueKind.Object ? target.GetIntCI("lineId") : 0;
        var openQty = target.ValueKind == JsonValueKind.Object ? target.GetDecimalCI("open") : 0m;
        if (lineId <= 0 || openQty < 2)
        {
            Fail(steps, "Açık sipariş kalemlerini okuma", $"'{item2Code}' için kısmi teslimata uygun açık kalem bulunamadı (lineId={lineId}, open={openQty}).");
            return;
        }
        var partialQty = Math.Floor(openQty / 2m);
        if (partialQty <= 0) partialQty = 1m;

        var before = FunctionalTestHelpers.BalanceFor(await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { item2Id }, ct), item2Id, loc1);

        var (delOk, delJson) = await StepPostAsync(ctx, steps, "Kısmi teslimat (İrsaliye oluşturma)", "/Warehouse/DeliverSalesOrderJson",
            new { orderId, lines = new object[] { new { lineId, quantity = partialQty } } }, ct);
        if (!delOk) return;
        var docNo = delJson.GetStringCI("docNo");
        if (string.IsNullOrWhiteSpace(docNo)) { Fail(steps, "Kısmi teslimat (İrsaliye oluşturma)", "Sunucu docNo döndürmedi."); return; }

        var (reopenOk, reopenJson) = await StepGetAsync(ctx, steps, "Kalan miktar doğrulama", $"/Warehouse/OrderOpenLinesJson?orderId={orderId}", ct);
        if (!reopenOk) return;
        var stillOpen = reopenJson.GetArrayCI("lines").FirstOrDefault(l => l.GetIntCI("lineId") == lineId);
        var remainingOpen = stillOpen.ValueKind == JsonValueKind.Object ? stillOpen.GetDecimalCI("open") : -1m;
        var expectedRemaining = openQty - partialQty;
        if (Math.Abs(remainingOpen - expectedRemaining) > 0.0001m || remainingOpen <= 0)
        {
            Fail(steps, "Kalan miktar doğrulama", $"Kısmi teslimat sonrası kalan {remainingOpen}, beklenen {expectedRemaining} (>0 olmalı).");
            return;
        }
        Pass(steps, "Kalan miktar doğrulama", $"Belge {docNo}, teslim={partialQty}, kalan={remainingOpen}.");

        var after = FunctionalTestHelpers.BalanceFor(await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { item2Id }, ct), item2Id, loc1);
        if (Math.Abs((before - partialQty) - after) > 0.0001m)
        {
            Fail(steps, "Bakiye doğrulama", $"Depo 1 item2 bakiyesi {before}→{after}, beklenen {before - partialQty}.");
            return;
        }
        Pass(steps, "Bakiye doğrulama", $"Depo 1 item2: {before} → {after}.");
    }
}
