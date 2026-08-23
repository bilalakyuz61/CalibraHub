using CalibraHub.Web.Services.FunctionalTests.Scenarios;

namespace CalibraHub.Web.Services.FunctionalTests;

/// <summary>
/// Faz 1 (ticari) senaryo kataloğu. Üretim/kalite senaryolarını ekleyecek ikinci ajan kendi
/// <c>Scenarios/*.cs</c> dosyalarını yazıp buraya (veya ayrı bir registry parçasına) ekler —
/// <see cref="FunctionalTestRunner"/> ve NDJSON kontratı DEĞİŞMEDEN yeni Group ("uretim"/"kalite")
/// değerleriyle çalışır.
/// </summary>
public static class FunctionalTestScenarioRegistry
{
    public static IReadOnlyList<IFunctionalTestScenario> BuildAll() => new IFunctionalTestScenario[]
    {
        // ── Hazırlık ──
        new SeedMasterDataScenario(),

        // ── Ambar (bağımsız belge tipleri) ──
        new StockInReceiptScenario(),
        new StockOutIssueScenario(),
        new StockTransferScenario(),
        new InventoryCountApplyScenario(),

        // ── İhtiyaç Kaydı ve karşılama akışları ──
        new NeedRecordCreateScenario(),
        new NeedRecordTransferScenario(),
        new NeedRecordStockIssueScenario(),
        new NeedRecordPurchaseDemandScenario(),

        // ── Satın alma zinciri (İhtiyaç Kaydı → Satın Alma Talebi → Alış Teklifi → Alış Siparişi → Alış İrsaliyesi) ──
        new PurchaseQuoteFromDemandScenario(),
        new PurchaseOrderFromQuoteScenario(),
        new PurchaseDeliveryFromOrderScenario(),

        // ── Satış zinciri (Teklif → Sipariş → İrsaliye tam/kısmi) ──
        new SalesQuoteCreateScenario(),
        new SalesOrderFromQuoteScenario(),
        new SalesDeliveryFullScenario(),
        new SalesDeliveryPartialScenario(),
    };
}
