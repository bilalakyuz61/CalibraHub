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

        // ── Tanımlamalar (departman → onay akışı, belge dizaynı) ──
        new DepartmentDefineScenario(),
        new ApprovalFlowDefineScenario(),
        new DocLayoutDefineScenario(),

        // ── Stok kombinasyonu (özellik → değer → kombinasyon üretme) ──
        new CombinationFeatureDefineScenario(),
        new CombinationGenerateScenario(),

        // ── Kit / paket ürün (tanım → sipariş → teslimatta bileşene patlatma) ──
        new KitDefineScenario(),
        new KitOrderCreateScenario(),
        new KitDeliveryScenario(),

        // ── Üretim (reçete → rota → iş emri → akış → sarf → mamul girişi) ──
        new ProductionSeedScenario(),
        new ProductionBomDefineScenario(),
        new ProductionRoutingDefineScenario(),
        new ProductionWorkOrderCreateScenario(),
        new ProductionFlowRecordScenario(),
        new ProductionConsumptionScenario(),
        new ProductionCompletionScenario(),

        // ── Yetki (sınırlı yetkili kullanıcı → engel / izin / iptal) ──
        new PermissionSeedScenario(),
        new PermissionDenyScenario(),
        new PermissionGrantScenario(),
        new PermissionRevokeScenario(),
        new PermissionPrecedenceScenario(),
        new PermissionGroupDormantScenario(),
    };
}
