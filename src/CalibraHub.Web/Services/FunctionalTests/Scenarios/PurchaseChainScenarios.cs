using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>Satın Alma Talebi → Alış Teklifi dönüşümü (tek-belge dönüşüm ucu, ConvertPurchaseDocJson).</summary>
public sealed class PurchaseQuoteFromDemandScenario : FunctionalTestScenarioBase
{
    public override string Key => "PURCHASE_QUOTE_FROM_DEMAND";
    public override string Group => "ticari";
    public override string Label => "Satın Alma Talebi → Alış Teklifi";
    public override IReadOnlyList<string> DependsOn => new[] { "NEED_RECORD_PURCHASE_DEMAND" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var demandDocId = ctx.GetInt(FunctionalTestContext.Keys.PurchaseDemandDocId);

        var (convOk, convJson) = await StepPostAsync(ctx, steps, "Alış Teklifine dönüştürme",
            $"/Sales/ConvertPurchaseDocJson?sourceId={demandDocId}", null, ct);
        if (!convOk) return;
        var quoteDocId = convJson.GetIntCI("id");
        if (quoteDocId <= 0) { Fail(steps, "Alış Teklifine dönüştürme", "Sunucu id döndürmedi."); return; }
        ctx.Set(FunctionalTestContext.Keys.PurchaseQuoteDocId, quoteDocId);

        var expectedTypeId = await ctx.ResolveDocumentTypeIdAsync("alis_teklifi", ct);
        var (getOk, getJson) = await StepGetAsync(ctx, steps, "Alış Teklifi doğrulama", $"/Sales/GetQuote?id={quoteDocId}", ct);
        if (!getOk) return;
        var quote = getJson.TryGetPropertyCI("quote", out var q) ? q : default;
        var actualTypeId = quote.GetIntOrNullCI("documentTypeId");
        var lineCount = getJson.GetArrayCI("lines").Count;
        if (actualTypeId != expectedTypeId || lineCount != 1)
        {
            Fail(steps, "Alış Teklifi doğrulama",
                $"Beklenen tip Id={expectedTypeId} kalem=1, gerçek tip Id={actualTypeId} kalem={lineCount}.");
            return;
        }
        Pass(steps, "Alış Teklifi doğrulama", $"Belge #{quoteDocId}, 1 kalem, tip doğru.");
    }
}

/// <summary>
/// Alış Teklifi → Alış Siparişi dönüşümü. ConvertDocumentsAsync bu geçişte cari + Onaylı durum
/// zorunlu tutar (reqApproved=true, reqContact=true) — İhtiyaç Kaydı'ndan miras kalan cari-siz
/// teklif, dönüştürmeden önce SaveDocument (update) ile cari + hedef depo atanarak tamamlanır;
/// bu da gerçek "kullanıcı Alış Teklifi ekranında cariyi seçip kaydeder" adımının HTTP karşılığıdır.
/// </summary>
public sealed class PurchaseOrderFromQuoteScenario : FunctionalTestScenarioBase
{
    public override string Key => "PURCHASE_ORDER_FROM_QUOTE";
    public override string Group => "ticari";
    public override string Label => "Alış Teklifi → Alış Siparişi";
    public override IReadOnlyList<string> DependsOn => new[] { "PURCHASE_QUOTE_FROM_DEMAND" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var quoteDocId = ctx.GetInt(FunctionalTestContext.Keys.PurchaseQuoteDocId);
        var contactId = ctx.GetInt(FunctionalTestContext.Keys.ContactId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);

        var (getOk, getJson) = await StepGetAsync(ctx, steps, "Alış Teklifi mevcut durumu okuma", $"/Sales/GetQuote?id={quoteDocId}", ct);
        if (!getOk) return;
        var quote = getJson.TryGetPropertyCI("quote", out var q) ? q : default;
        var lines = getJson.GetArrayCI("lines");
        if (quote.ValueKind != JsonValueKind.Object || lines.Count == 0)
        {
            Fail(steps, "Alış Teklifi mevcut durumu okuma", "Belge veya kalemler okunamadı.");
            return;
        }

        var linesPayload = lines.Select(l => (object)new
        {
            id = l.GetIntCI("id"),
            itemId = l.GetIntCI("itemId"),
            unitId = l.GetIntOrNullCI("unitId"),
            quantity = l.GetDecimalCI("quantity"),
            unitPrice = l.GetDecimalCI("unitPrice"),
            discountRate = l.GetDecimalCI("discountRate"),
            combinationId = l.GetIntOrNullCI("combinationId"),
            locationId = loc1,
            notes = l.GetStringCI("notes"),
        }).ToArray();

        var (updateOk, _) = await StepPostAsync(ctx, steps, "Alış Teklifine cari + depo atama (güncelleme)", "/Sales/SaveDocument",
            new
            {
                id = quoteDocId,
                documentDate = quote.GetStringCI("documentDate") is { } dd ? DateTime.Parse(dd) : DateTime.Today,
                validUntil = (DateTime?)null,
                contactId, contactName = (string?)null, contactAddress = (string?)null,
                salesRepId = quote.GetIntOrNullCI("salesRepId"),
                currencyId = quote.GetIntCI("currencyId"),
                discountRate = quote.GetDecimalCI("discountRate"), taxRate = quote.GetDecimalCI("taxRate"),
                paymentTerms = quote.GetStringCI("paymentTerms"), deliveryTerms = quote.GetStringCI("deliveryTerms"),
                deliveryAddress = quote.GetStringCI("deliveryAddress"), notes = quote.GetStringCI("notes"),
                lines = linesPayload,
                contactCode = (string?)null, documentTypeId = quote.GetIntOrNullCI("documentTypeId"),
                deliveryDate = (DateTime?)null, deliveryDays = (int?)null, requesterPersonnelId = (int?)null,
                fromRequestId = (int?)null, locationId = loc1,
                exchangeRate = quote.GetDecimalCI("exchangeRate", 1m), isVatIncluded = quote.GetBoolCI("isVatIncluded") ?? false,
                rateDate = (DateTime?)null, sourceDocumentNo = (string?)null,
            }, ct);
        if (!updateOk) return;

        var (approveOk, _) = await StepPostAsync(ctx, steps, "Alış Teklifini onaylama", "/Sales/ChangeQuoteStatus",
            new { id = quoteDocId, status = "Approved" }, ct);
        if (!approveOk) return;

        var (convOk, convJson) = await StepPostAsync(ctx, steps, "Alış Siparişine dönüştürme",
            $"/Sales/ConvertPurchaseDocJson?sourceId={quoteDocId}", null, ct);
        if (!convOk) return;
        var orderDocId = convJson.GetIntCI("id");
        if (orderDocId <= 0) { Fail(steps, "Alış Siparişine dönüştürme", "Sunucu id döndürmedi."); return; }
        ctx.Set(FunctionalTestContext.Keys.PurchaseOrderDocId, orderDocId);

        var expectedTypeId = await ctx.ResolveDocumentTypeIdAsync("alis_siparisi", ct);
        var (checkOk, checkJson) = await StepGetAsync(ctx, steps, "Alış Siparişi doğrulama", $"/Sales/GetQuote?id={orderDocId}", ct);
        if (!checkOk) return;
        var orderDoc = checkJson.TryGetPropertyCI("quote", out var oq) ? oq : default;
        var actualTypeId = orderDoc.GetIntOrNullCI("documentTypeId");
        var actualContactId = orderDoc.GetIntOrNullCI("contactId");
        if (actualTypeId != expectedTypeId || actualContactId != contactId)
        {
            Fail(steps, "Alış Siparişi doğrulama",
                $"Beklenen tip Id={expectedTypeId} cari={contactId}, gerçek tip Id={actualTypeId} cari={actualContactId}.");
            return;
        }
        Pass(steps, "Alış Siparişi doğrulama", $"Belge #{orderDocId}.");
    }
}

/// <summary>Alış Siparişi → Alış İrsaliyesi (mal kabul, ReceivePurchaseOrderJson) — stok girişini bakiye üzerinden doğrular.</summary>
public sealed class PurchaseDeliveryFromOrderScenario : FunctionalTestScenarioBase
{
    public override string Key => "PURCHASE_DELIVERY_FROM_ORDER";
    public override string Group => "ticari";
    public override string Label => "Alış Siparişi → Alış İrsaliyesi (Mal Kabul)";
    public override IReadOnlyList<string> DependsOn => new[] { "PURCHASE_ORDER_FROM_QUOTE" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var orderDocId = ctx.GetInt(FunctionalTestContext.Keys.PurchaseOrderDocId);
        var item1Id = ctx.GetInt(FunctionalTestContext.Keys.Item1Id);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);
        var qty = NeedRecordCreateScenario.PurchaseLineQty;

        var before = FunctionalTestHelpers.BalanceFor(await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { item1Id }, ct), item1Id, loc1);

        var (recvOk, recvJson) = await StepPostAsync(ctx, steps, "Mal kabul (Alış İrsaliyesi oluşturma)", "/Warehouse/ReceivePurchaseOrderJson",
            new { orderId = orderDocId, lines = (object?)null }, ct);
        if (!recvOk) return;
        var docNo = recvJson.GetStringCI("docNo");
        if (string.IsNullOrWhiteSpace(docNo)) { Fail(steps, "Mal kabul (Alış İrsaliyesi oluşturma)", "Sunucu docNo döndürmedi."); return; }

        var after = FunctionalTestHelpers.BalanceFor(await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { item1Id }, ct), item1Id, loc1);
        if (Math.Abs((before + qty) - after) > 0.0001m)
        {
            Fail(steps, "Bakiye doğrulama", $"Depo 1 item1 bakiyesi {before}→{after}, beklenen {before + qty}. " +
                "(Sipariş/teklif zincirinde depo ataması eksik kalmış olabilir.)");
            return;
        }
        Pass(steps, "Bakiye doğrulama", $"Belge {docNo}, Depo 1 item1: {before} → {after}.");
    }
}
