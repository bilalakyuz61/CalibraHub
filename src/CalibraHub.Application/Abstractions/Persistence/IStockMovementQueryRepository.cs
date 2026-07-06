using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Malzeme kartı "Stok Hareketleri" sekmesi — DocumentLine (MovementType dolu) tabanlı
/// hareket ekstresi. StockMovement tablosu 2026-07-02'de emekliye ayrıldı; hareketler
/// Document + DocumentLine üzerinden okunur. Koşan bakiye tüm hareket geçmişi üzerinden
/// hesaplanır (kronolojik), filtreler yalnızca gösterimi daraltır — her satır kendi
/// gerçek bakiyesini taşır.
/// </summary>
public interface IStockMovementQueryRepository
{
    Task<ItemStockMovementResultDto> ListForItemAsync(ItemStockMovementFilter filter, CancellationToken ct);
}
