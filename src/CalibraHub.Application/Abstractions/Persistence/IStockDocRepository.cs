using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IStockDocRepository
{
    Task<IReadOnlyList<StockDocDto>> GetByTypeAsync(string docType, CancellationToken ct);
    Task<IReadOnlyList<StockDocDto>> GetByTypesAsync(IEnumerable<string> docTypes, CancellationToken ct);
    Task<StockDocDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<StockDocLineDto>> GetLinesAsync(int docId, CancellationToken ct);
    Task<(int Id, string DocNo)> SaveAsync(SaveStockDocRequest request, int? createdById, CancellationToken ct);

    /// <summary>
    /// Satış siparişi → Satış İrsaliyesi (stok etkili teslimat): siparişin açık (teslim edilmemiş)
    /// kalemleri için tek transaction'da satis_irsaliyesi belgesi + ana birimde çıkış satırları
    /// (MovementType=1, FromLocationId) yazar, sipariş satırlarının DeliveredQuantity'sini
    /// BaseQuantity'ye çeker (açık miktar → 0) ve eksi bakiye kontrolünü uygular. Yetersiz stokta
    /// NegativeBalanceException fırlatır (tx geri alınır). İrsaliye siparişe ParentDocumentId +
    /// kalem SourceLineId ile bağlanır; cari/tutar alanları siparişten kopyalanır.
    /// </summary>
    Task<(int Id, string DocNo)> DeliverSalesOrderAsync(int salesOrderId, int? createdById, CancellationToken ct);

    /// <summary>
    /// Satın alma siparişi → Alış İrsaliyesi (stok etkili mal kabul): açık sipariş kalemleri için
    /// alis_irsaliyesi belgesi + ana birimde giriş satırları (MovementType=2, LocationId) yazar,
    /// sipariş satırlarının DeliveredQuantity'sini BaseQuantity'ye çeker. Giriş bakiyeyi artırdığı
    /// için eksi bakiye kontrolü uygulanmaz. İrsaliye siparişe ParentDocumentId + kalem SourceLineId
    /// ile bağlanır; cari/tutar alanları siparişten kopyalanır.
    /// </summary>
    Task<(int Id, string DocNo)> ReceivePurchaseOrderAsync(int purchaseOrderId, int? createdById, CancellationToken ct);

    /// <summary>
    /// Üretim sarfı (iş emri bileşen sarfı, 2026-07-10) — tek transaction'da: iş emrinin
    /// belgesine MovementType=1 (çıkış) satırları yazar; lot/seri kurallarını stok çıkışıyla
    /// birebir uygular (lot-takipli → mevcut lot zorunlu, seri-takipli → stoktaki InStock
    /// serilerden adet kadar seçim → Issued); WorkOrderComponent.IssuedQuantity'yi artırır
    /// (serbest satırda RequiredQuantity=0 "Serbest sarf" bileşen kaydı açılır) ve eksi
    /// bakiye + lot bakiye kontrollerini çalıştırır. Yazılan satır sayısını döner.
    /// </summary>
    Task<int> IssueWorkOrderConsumptionAsync(WorkOrderConsumptionRequest request, int? createdById, CancellationToken ct);

    Task DeleteAsync(int id, CancellationToken ct);
}
