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

    Task DeleteAsync(int id, CancellationToken ct);
}
