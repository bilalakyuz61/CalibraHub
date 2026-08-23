using System.Text.Json;

namespace CalibraHub.Web.Services.FunctionalTests.Scenarios;

/// <summary>
/// Ambar Giriş Fişi — SEED'in ürettiği 2 malzemeyi Depo 1'e büyük miktarda (200 adet) sokar.
/// Bu bakiye, kendisine bağımlı TÜM sonraki senaryolar (transfer/çıkış/sipariş teslimatı/
/// sayım) için negatif bakiye korumasına takılmayacak kadar geniş tutulmuştur.
/// </summary>
public sealed class StockInReceiptScenario : FunctionalTestScenarioBase
{
    public const decimal ReceiptQty = 200m;

    public override string Key => "STOCK_IN_RECEIPT";
    public override string Group => "ticari";
    public override string Label => "Ambar Giriş Fişi";
    public override IReadOnlyList<string> DependsOn => new[] { "SEED_MASTER_DATA" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var item1Id = ctx.GetInt(FunctionalTestContext.Keys.Item1Id);
        var item2Id = ctx.GetInt(FunctionalTestContext.Keys.Item2Id);
        var unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);

        var (saveOk, saveJson) = await StepPostAsync(ctx, steps, "Giriş fişi kaydetme", "/Warehouse/SaveDocJson",
            new
            {
                id = (int?)null, docType = "STOCK_IN", docNo = (string?)null, docDate = DateTime.Today,
                fromLocationId = (int?)null, toLocationId = loc1, refNo = (string?)null, notes = "Fonksiyon testi — ambar girişi",
                lines = new object[]
                {
                    new { id = (int?)null, itemId = item1Id, unitId, qty = ReceiptQty, combinationId = (int?)null, notes = (string?)null, fromLocationId = (int?)null, toLocationId = loc1, unitCost = (decimal?)null },
                    new { id = (int?)null, itemId = item2Id, unitId, qty = ReceiptQty, combinationId = (int?)null, notes = (string?)null, fromLocationId = (int?)null, toLocationId = loc1, unitCost = (decimal?)null },
                },
                argeProjectId = (int?)null,
            }, ct);
        if (!saveOk) return;
        var docId = saveJson.GetIntCI("id");
        if (docId <= 0) { Fail(steps, "Giriş fişi kaydetme", "Sunucu belge Id'si döndürmedi."); return; }
        ctx.Set(FunctionalTestContext.Keys.StockInDocId, docId);

        var balances = await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { item1Id, item2Id }, ct);
        var bal1 = FunctionalTestHelpers.BalanceFor(balances, item1Id, loc1);
        var bal2 = FunctionalTestHelpers.BalanceFor(balances, item2Id, loc1);
        if (bal1 < ReceiptQty || bal2 < ReceiptQty)
        {
            Fail(steps, "Bakiye doğrulama", $"Depo 1 bakiyesi beklenenin altında — item1={bal1}, item2={bal2}, beklenen>={ReceiptQty}.");
            return;
        }
        Pass(steps, "Bakiye doğrulama", $"Depo 1: item1={bal1}, item2={bal2}.");
    }
}

/// <summary>Ambar Çıkış Fişi — Depo 1'den item1'den 5 adet düşer, bakiye azalışını doğrular.</summary>
public sealed class StockOutIssueScenario : FunctionalTestScenarioBase
{
    private const decimal IssueQty = 5m;

    public override string Key => "STOCK_OUT_ISSUE";
    public override string Group => "ticari";
    public override string Label => "Ambar Çıkış Fişi";
    public override IReadOnlyList<string> DependsOn => new[] { "STOCK_IN_RECEIPT" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var item1Id = ctx.GetInt(FunctionalTestContext.Keys.Item1Id);
        var unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);

        var before = FunctionalTestHelpers.BalanceFor(await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { item1Id }, ct), item1Id, loc1);

        var (saveOk, saveJson) = await StepPostAsync(ctx, steps, "Çıkış fişi kaydetme", "/Warehouse/SaveDocJson",
            new
            {
                id = (int?)null, docType = "STOCK_OUT", docNo = (string?)null, docDate = DateTime.Today,
                fromLocationId = loc1, toLocationId = (int?)null, refNo = (string?)null, notes = "Fonksiyon testi — ambar çıkışı",
                lines = new object[]
                {
                    new { id = (int?)null, itemId = item1Id, unitId, qty = IssueQty, combinationId = (int?)null, notes = (string?)null, fromLocationId = loc1, toLocationId = (int?)null, unitCost = (decimal?)null },
                },
                argeProjectId = (int?)null,
            }, ct);
        if (!saveOk) return;
        var docId = saveJson.GetIntCI("id");
        if (docId <= 0) { Fail(steps, "Çıkış fişi kaydetme", "Sunucu belge Id'si döndürmedi."); return; }

        var after = FunctionalTestHelpers.BalanceFor(await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { item1Id }, ct), item1Id, loc1);
        if (Math.Abs((before - IssueQty) - after) > 0.0001m)
        {
            Fail(steps, "Bakiye doğrulama", $"Beklenen {before - IssueQty}, gerçek {after} (önce={before}).");
            return;
        }
        Pass(steps, "Bakiye doğrulama", $"Depo 1 item1: {before} → {after}.");
    }
}

/// <summary>Depolar Arası Transfer — Depo 1 → Depo 2, item2'den 5 adet. İki depodaki bakiye değişimini birlikte doğrular.</summary>
public sealed class StockTransferScenario : FunctionalTestScenarioBase
{
    private const decimal TransferQty = 5m;

    public override string Key => "STOCK_TRANSFER";
    public override string Group => "ticari";
    public override string Label => "Depolar Arası Transfer";
    public override IReadOnlyList<string> DependsOn => new[] { "STOCK_IN_RECEIPT" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var item2Id = ctx.GetInt(FunctionalTestContext.Keys.Item2Id);
        var unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);
        var loc2 = ctx.GetInt(FunctionalTestContext.Keys.Location2Id);

        var beforeBalances = await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { item2Id }, ct);
        var before1 = FunctionalTestHelpers.BalanceFor(beforeBalances, item2Id, loc1);
        var before2 = FunctionalTestHelpers.BalanceFor(beforeBalances, item2Id, loc2);

        var (saveOk, saveJson) = await StepPostAsync(ctx, steps, "Transfer fişi kaydetme", "/Warehouse/SaveDocJson",
            new
            {
                id = (int?)null, docType = "TRANSFER", docNo = (string?)null, docDate = DateTime.Today,
                fromLocationId = (int?)null, toLocationId = (int?)null, refNo = (string?)null, notes = "Fonksiyon testi — depolar arası transfer",
                lines = new object[]
                {
                    new { id = (int?)null, itemId = item2Id, unitId, qty = TransferQty, combinationId = (int?)null, notes = (string?)null, fromLocationId = loc1, toLocationId = loc2, unitCost = (decimal?)null },
                },
                argeProjectId = (int?)null,
            }, ct);
        if (!saveOk) return;
        var docId = saveJson.GetIntCI("id");
        if (docId <= 0) { Fail(steps, "Transfer fişi kaydetme", "Sunucu belge Id'si döndürmedi."); return; }

        var afterBalances = await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { item2Id }, ct);
        var after1 = FunctionalTestHelpers.BalanceFor(afterBalances, item2Id, loc1);
        var after2 = FunctionalTestHelpers.BalanceFor(afterBalances, item2Id, loc2);

        if (Math.Abs((before1 - TransferQty) - after1) > 0.0001m || Math.Abs((before2 + TransferQty) - after2) > 0.0001m)
        {
            Fail(steps, "Bakiye doğrulama", $"Depo1 {before1}→{after1} (beklenen {before1 - TransferQty}), Depo2 {before2}→{after2} (beklenen {before2 + TransferQty}).");
            return;
        }
        Pass(steps, "Bakiye doğrulama", $"Depo1: {before1}→{after1}, Depo2: {before2}→{after2}.");
    }
}

/// <summary>
/// Sayım ve Yansıtma — Depo 1'deki item1 güncel bakiyesinin 3 eksiğini sayar, "Yansıt"
/// (ApplyInventoryJson) çağırır, bakiyenin sayılan değere eşitlendiğini doğrular.
/// </summary>
public sealed class InventoryCountApplyScenario : FunctionalTestScenarioBase
{
    public override string Key => "INVENTORY_COUNT_APPLY";
    public override string Group => "ticari";
    public override string Label => "Sayım ve Yansıtma (Depo Sayım Fişi)";
    public override IReadOnlyList<string> DependsOn => new[] { "STOCK_IN_RECEIPT" };

    protected override async Task ExecuteAsync(FunctionalTestContext ctx, List<FunctionalTestStep> steps, CancellationToken ct)
    {
        var item1Id = ctx.GetInt(FunctionalTestContext.Keys.Item1Id);
        var unitId = ctx.GetInt(FunctionalTestContext.Keys.UnitId);
        var loc1 = ctx.GetInt(FunctionalTestContext.Keys.Location1Id);

        var before = FunctionalTestHelpers.BalanceFor(await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { item1Id }, ct), item1Id, loc1);
        if (before < 3m)
        {
            Fail(steps, "Sayım ön koşulu", $"Depo 1 item1 bakiyesi ({before}) sayım farkı simüle etmek için yetersiz (en az 3 gerekli).");
            return;
        }
        var countedQty = before - 3m;

        var (saveOk, saveJson) = await StepPostAsync(ctx, steps, "Sayım fişi kaydetme (taslak)", "/Warehouse/SaveInventoryJson",
            new
            {
                id = (int?)null, docType = "INVENTORY_COUNT", docNo = (string?)null, docDate = DateTime.Today,
                fromLocationId = loc1, toLocationId = (int?)null, refNo = (string?)null, notes = "Fonksiyon testi — sayım",
                lines = new object[]
                {
                    new { id = (int?)null, itemId = item1Id, unitId, qty = countedQty, combinationId = (int?)null, notes = (string?)null, fromLocationId = (int?)null, toLocationId = (int?)null, unitCost = (decimal?)null },
                },
                argeProjectId = (int?)null,
            }, ct);
        if (!saveOk) return;
        var countId = saveJson.GetIntCI("id");
        if (countId <= 0) { Fail(steps, "Sayım fişi kaydetme (taslak)", "Sunucu belge Id'si döndürmedi."); return; }

        var (applyOk, _) = await StepPostAsync(ctx, steps, "Sayımı yansıtma (Uygula)", $"/Warehouse/ApplyInventoryJson?id={countId}", null, ct);
        if (!applyOk) return;

        var after = FunctionalTestHelpers.BalanceFor(await FunctionalTestHelpers.GetStockBalancesAsync(ctx, new[] { item1Id }, ct), item1Id, loc1);
        if (Math.Abs(after - countedQty) > 0.0001m)
        {
            Fail(steps, "Bakiye doğrulama", $"Yansıtma sonrası bakiye {after}, beklenen (sayılan) {countedQty}.");
            return;
        }
        Pass(steps, "Bakiye doğrulama", $"Depo 1 item1: {before} → {after} (sayılan {countedQty}).");
    }
}

/// <summary>Bu klasördeki tüm senaryoların ortak küçük yardımcıları — tekrar eden bakiye sorgusu deseni.</summary>
internal static class FunctionalTestHelpers
{
    public static async Task<IReadOnlyList<JsonElement>> GetStockBalancesAsync(
        FunctionalTestContext ctx, IReadOnlyList<int> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0) return Array.Empty<JsonElement>();
        var query = string.Join("&", itemIds.Select(id => $"itemIds={id}"));
        var res = await ctx.Http.GetAsync($"/Purchase/StockBalances?{query}", ct);
        return res.Ok ? res.Json.AsArray() : Array.Empty<JsonElement>();
    }

    public static decimal BalanceFor(IReadOnlyList<JsonElement> balances, int itemId, int locationId)
        => balances.Where(b => b.GetIntCI("itemId") == itemId && b.GetIntCI("locationId") == locationId)
                   .Select(b => b.GetDecimalCI("balance"))
                   .DefaultIfEmpty(0m)
                   .Sum();

    /// <summary>
    /// İhtiyaç Kaydı (alis_talebi) satırının karşılama defteri özeti — /Purchase/RequestLines
    /// üzerinden "remaining" (Quantity − FulfilledFromStock − FulfilledByPurchase) okur.
    /// Satır bulunamazsa null (çağıran bunu ayrı bir başarısızlık adımı olarak raporlar).
    /// </summary>
    public static async Task<decimal?> GetRequestLineRemainingAsync(
        FunctionalTestContext ctx, int requestDocId, int lineId, CancellationToken ct)
    {
        var res = await ctx.Http.GetAsync($"/Purchase/RequestLines?requestIds={requestDocId}", ct);
        if (!res.Ok) return null;
        var row = res.Json.AsArray().FirstOrDefault(r => r.GetIntCI("lineId") == lineId);
        return row.ValueKind == JsonValueKind.Object ? row.GetDecimalCI("remaining") : (decimal?)null;
    }
}
