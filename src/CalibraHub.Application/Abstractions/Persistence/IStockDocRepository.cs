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
    /// Satış siparişi teslimatı (Faz 2 rezervasyon): siparişin açık (teslim edilmemiş) kalemleri
    /// için tek transaction'da fiziksel çıkış (STOCK_OUT/depo_cikis, MovementType=1) yazar,
    /// sipariş satırlarının DeliveredQuantity'sini BaseQuantity'ye çeker (açık miktar → 0, rezervasyon
    /// serbest) ve eksi bakiye kontrolünü uygular. Yetersiz stokta NegativeBalanceException fırlatır
    /// (tx geri alınır). Çıkış belgesi siparişe ParentDocumentId ile bağlanır.
    /// </summary>
    Task<(int Id, string DocNo)> DeliverSalesOrderAsync(int salesOrderId, int? createdById, CancellationToken ct);

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
