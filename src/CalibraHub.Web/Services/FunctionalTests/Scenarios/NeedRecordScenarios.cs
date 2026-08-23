using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// İhtiyaç Kaydı (alis_talebi) oluşturma — 3 kalemli bir belge kaydeder, her kalemi ayrı bir
/// sonraki senaryoya (depo transferi / ambar çıkış fişi / satın alma zinciri) ayırır ki
/// kalemler birbirini etkilemesin (her senaryo kendi kalemini bağımsız doğrulayabilsin).
/// Kaydettikten sonra belgeyi Onaylandı durumuna çeker — CLAUDE.md: fresh test şirketinde
/// hiçbir onay akışı tanımlı değildir (IsApprovalGovernedAsync=false) ama
/// PurchaseController.CheckFulfillmentApprovalGuardAsync varsayılan olarak (parametre
/// tanımsızken) onayı ZORUNLU sayar — bu adım atlanırsa sonraki 3 senaryo da
/// "onaylanmadan karşılama yapılamaz" hatasıyla kırılır.
/// </summary>
public sealed class NeedRecordCreateScenario : FunctionalTestScenarioBase
{
    public const decimal TransferLineQty = 10m;
    public const decimal StockIssueLineQty = 8m;
    public const decimal PurchaseLineQty = 12m;

    public override string Key => "NEED_RECORD_CREATE";
    public override string Group => "ticari";
    public override string Label => "İhtiyaç Kaydı Oluşturma";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var item1Id = ctx.GetInt(FunctionalTestContext.Keys.Item1Id);
        var item2Id = ctx.GetInt(FunctionalTestContext.Keys.Item2Id);
        var unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);
        var personnelId = ctx.GetInt(FunctionalTestContext.Keys.PersonnelId);
        var currencyId = ctx.GetInt(FunctionalTestContext.Keys.CurrencyId);

        var typeId = await ctx.ResolveDocumentTypeIdAsync("alis_talebi", ct);
        if (typeId is not > 0) { Fail(steps, "Belge tipi çözümleme (alis_talebi)", "'alis_talebi' DocumentType kaydı bulunamadı."); return; }

        var (saveOk, saveJson) = await StepPostAsync(ctx, steps, "İhtiyaç kaydı oluşturma", "/Sales/SaveDocument",
            new
            {
                id = (int?)null, documentDate = DateTime.Today, validUntil = (DateTime?)null,
                contactId = (int?)null, contactName = (string?)null, contactAddress = (string?)null,
                salesRepId = (int?)null, currencyId, discountRate = 0m, taxRate = 0m,
                paymentTerms = (string?)null, deliveryTerms = (string?)null, deliveryAddress = (string?)null,
                notes = "Fonksiyon testi — İhtiyaç Kaydı",
                lines = new object[]
                {
                    new { id = (int?)null, itemId = item1Id, unitId, quantity = TransferLineQty, unitPrice = 0m, discountRate = 0m, combinationId = (int?)null, locationId = loc1, notes = (string?)null },
                    new { id = (int?)null, itemId = item2Id, unitId, quantity = StockIssueLineQty, unitPrice = 0m, discountRate = 0m, combinationId = (int?)null, locationId = loc1, notes = (string?)null },
                    new { id = (int?)null, itemId = item1Id, unitId, quantity = PurchaseLineQty, unitPrice = 0m, discountRate = 0m, combinationId = (int?)null, locationId = loc1, notes = (string?)null },
                },
                contactCode = (string?)null, documentTypeId = typeId, deliveryDate = (DateTime?)null, deliveryDays = (int?)null,
                requesterPersonnelId = personnelId, fromRequestId = (int?)null, locationId = loc1,
                exchangeRate = 1m, isVatIncluded = false, rateDate = (DateTime?)null, sourceDocumentNo = (string?)null,
            }, ct);
        if (!saveOk) return;

        var quote = saveJson.TryGetPropertyCI("quote", out var quoteEl) ? quoteEl : default;
        var docId = quote.ValueKind == JsonValueKind.Object ? quote.GetIntCI("id") : 0;
        if (docId <= 0) { Fail(steps, "İhtiyaç kaydı oluşturma", "Sunucu quote.id döndürmedi."); return; }
        ctx.Set(FunctionalTestContext.Keys.NeedDocId, docId);

        var lines = saveJson.GetArrayCI("lines");
        if (lines.Count != 3) { Fail(steps, "Kalem doğrulama", $"3 kalem beklenirken {lines.Count} kalem döndü."); return; }

        int FindLineId(int itemId, decimal qty) => lines
            .Where(l => l.GetIntCI("itemId") == itemId && Math.Abs(l.GetDecimalCI("quantity") - qty) < 0.0001m)
            .Select(l => l.GetIntCI("id"))
            .FirstOrDefault();

        var transferLineId = FindLineId(item1Id, TransferLineQty);
        var stockIssueLineId = FindLineId(item2Id, StockIssueLineQty);
        var purchaseLineId = FindLineId(item1Id, PurchaseLineQty);
        if (transferLineId <= 0 || stockIssueLineId <= 0 || purchaseLineId <= 0)
        {
            Fail(steps, "Kalem doğrulama", $"Kalem Id'leri eşleştirilemedi — transfer={transferLineId}, stockIssue={stockIssueLineId}, purchase={purchaseLineId}.");
            return;
        }
        ctx.Set(FunctionalTestContext.Keys.NeedLineTransferId, transferLineId);
        ctx.Set(FunctionalTestContext.Keys.NeedLineStockIssueId, stockIssueLineId);
        ctx.Set(FunctionalTestContext.Keys.NeedLinePurchaseId, purchaseLineId);
        Pass(steps, "Kalem doğrulama", $"3 kalem oluştu (Id: {transferLineId}, {stockIssueLineId}, {purchaseLineId}).");

        var (approveOk, _) = await StepPostAsync(ctx, steps, "İhtiyaç kaydını onaylama", "/Sales/ChangeQuoteStatus",
            new { id = docId, status = "Approved" }, ct);
        if (!approveOk) return;

        var (getOk, getJson) = await StepGetAsync(ctx, steps, "Onay durumu doğrulama", $"/Sales/GetQuote?id={docId}", ct);
        if (!getOk) return;
        var status = getJson.TryGetPropertyCI("quote", out var q2) ? q2.GetStringCI("status") : null;
        if (!string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            Fail(steps, "Onay durumu doğrulama", $"Beklenen 'Approved', gerçek '{status}'.");
            return;
        }
        Pass(steps, "Onay durumu doğrulama");
    }
}

/// <summary>İhtiyaç Kaydından Depo Transferi — Depo 1 → Depo 2, kaynak satırın karşılama bakiyesini doğrular.</summary>
public sealed class NeedRecordTransferScenario : FunctionalTestScenarioBase
{
    public override string Key => "NEED_RECORD_TRANSFER";
    public override string Group => "ticari";
    public override string Label => "İhtiyaç Kaydından Depo Transferi";
    public override IReadOnlyList<string> DependsOn => new[] { "NEED_RECORD_CREATE", "STOCK_IN_RECEIPT" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var item1Id = ctx.GetInt(FunctionalTestContext.Keys.Item1Id);
        var unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);
        var loc2 = ctx.GetInt(FunctionalTestContext.Keys.Location2Id);
        var needDocId = ctx.GetInt(FunctionalTestContext.Keys.NeedDocId);
        var lineId = ctx.GetInt(FunctionalTestContext.Keys.NeedLineTransferId);
        var qty = NeedRecordCreateScenario.TransferLineQty;

        var (saveOk, _) = await StepPostAsync(ctx, steps, "Depo transferi oluşturma", "/Purchase/CreateTransfer",
            new
            {
                requestIds = new[] { needDocId }, notes = "Fonksiyon testi — İhtiyaç → Transfer",
                lines = new object[]
                {
                    new { itemId = item1Id, unitId, qty, fromLocationId = loc1, toLocationId = loc2, combinationId = (int?)null, notes = (string?)null, requestLineId = lineId },
                },
            }, ct);
        if (!saveOk) return;

        var remaining = await FunctionalTestHelpers.GetRequestLineRemainingAsync(ctx, needDocId, lineId, ct);
        if (remaining is null) { Fail(steps, "Karşılama defteri doğrulama", "İhtiyaç satırı /Purchase/RequestLines yanıtında bulunamadı."); return; }
        if (remaining.Value > 0.0001m)
        {
            Fail(steps, "Karşılama defteri doğrulama", $"Transfer sonrası kalan miktar {remaining.Value} (0 bekleniyordu).");
            return;
        }
        Pass(steps, "Karşılama defteri doğrulama", $"İhtiyaç satırı tam karşılandı (kalan={remaining.Value}).");
    }
}

/// <summary>İhtiyaç Kaydından Ambar Çıkış Fişi — FulfillFromStock (OverrideLocationId ile doğrudan Depo 1'den).</summary>
public sealed class NeedRecordStockIssueScenario : FunctionalTestScenarioBase
{
    public override string Key => "NEED_RECORD_STOCK_ISSUE";
    public override string Group => "ticari";
    public override string Label => "İhtiyaç Kaydından Ambar Çıkış Fişi";
    public override IReadOnlyList<string> DependsOn => new[] { "NEED_RECORD_CREATE", "STOCK_IN_RECEIPT" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);
        var needDocId = ctx.GetInt(FunctionalTestContext.Keys.NeedDocId);
        var lineId = ctx.GetInt(FunctionalTestContext.Keys.NeedLineStockIssueId);
        var qty = NeedRecordCreateScenario.StockIssueLineQty;

        var (saveOk, saveJson) = await StepPostAsync(ctx, steps, "Depodan karşılama (Ambar Çıkış Fişi)", "/Purchase/FulfillFromStock",
            new
            {
                lines = new object[] { new { lineId, qty } },
                notes = "Fonksiyon testi — İhtiyaç → Ambar Çıkış Fişi",
                overrideLocationId = loc1,
                cumulateItems = (bool?)null,
            }, ct);
        if (!saveOk) return;
        var docNo = saveJson.GetStringCI("docNo");
        if (string.IsNullOrWhiteSpace(docNo)) { Fail(steps, "Depodan karşılama (Ambar Çıkış Fişi)", "Sunucu docNo döndürmedi — kalem eşleşmemiş olabilir."); return; }

        var remaining = await FunctionalTestHelpers.GetRequestLineRemainingAsync(ctx, needDocId, lineId, ct);
        if (remaining is null) { Fail(steps, "Karşılama defteri doğrulama", "İhtiyaç satırı /Purchase/RequestLines yanıtında bulunamadı."); return; }
        if (remaining.Value > 0.0001m)
        {
            Fail(steps, "Karşılama defteri doğrulama", $"Ambar çıkışı sonrası kalan miktar {remaining.Value} (0 bekleniyordu).");
            return;
        }
        Pass(steps, "Karşılama defteri doğrulama", $"İhtiyaç satırı tam karşılandı (belge {docNo}, kalan={remaining.Value}).");
    }
}

/// <summary>İhtiyaç Kaydından Satın Alma Talebi (satin_alma_talebi) — satın alma zincirinin ilk halkası.</summary>
public sealed class NeedRecordPurchaseDemandScenario : FunctionalTestScenarioBase
{
    public override string Key => "NEED_RECORD_PURCHASE_DEMAND";
    public override string Group => "ticari";
    public override string Label => "İhtiyaç Kaydından Satın Alma Talebi";
    public override IReadOnlyList<string> DependsOn => new[] { "NEED_RECORD_CREATE" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var needDocId = ctx.GetInt(FunctionalTestContext.Keys.NeedDocId);
        var lineId = ctx.GetInt(FunctionalTestContext.Keys.NeedLinePurchaseId);

        var (saveOk, saveJson) = await StepPostAsync(ctx, steps, "Satın alma talebi oluşturma", "/Purchase/CreatePurchaseDemand",
            new
            {
                lineIds = new[] { lineId }, notes = "Fonksiyon testi — İhtiyaç → Satın Alma Talebi",
                lines = (object?)null,
            }, ct);
        if (!saveOk) return;
        var docId = saveJson.GetIntCI("docId");
        if (docId <= 0) { Fail(steps, "Satın alma talebi oluşturma", "Sunucu docId döndürmedi."); return; }
        ctx.Set(FunctionalTestContext.Keys.PurchaseDemandDocId, docId);

        var remaining = await FunctionalTestHelpers.GetRequestLineRemainingAsync(ctx, needDocId, lineId, ct);
        if (remaining is null) { Fail(steps, "Karşılama defteri doğrulama", "İhtiyaç satırı /Purchase/RequestLines yanıtında bulunamadı."); return; }
        if (remaining.Value > 0.0001m)
        {
            Fail(steps, "Karşılama defteri doğrulama", $"Satın alma talebi sonrası kalan miktar {remaining.Value} (0 bekleniyordu).");
            return;
        }
        Pass(steps, "Karşılama defteri doğrulama", $"Satın alma talebi #{docId} oluşturuldu, İhtiyaç satırı kalan={remaining.Value}.");
    }
}
