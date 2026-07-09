namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Lot (parti) ana kayıtları — lot-takipli stoklarda (Items.TrackingType='Lot') hareketler
/// Lot.Id üzerinden ilişkilenir (ID tabanlı eşleştirme kuralı; LotNo yalnız display).
/// Ambar belge kaydı (SqlStockDocRepository) kendi transaction'ı içinde inline çözümleme
/// yapar; bu arayüz transaction dışı akışlar (iş emri mamul girişi vb.) içindir.
/// </summary>
public interface ILotRepository
{
    /// <summary>Items.TrackingType değerini döndürür ("None" / "Lot" / "Serial"; stok yoksa null).</summary>
    Task<string?> GetItemTrackingTypeAsync(int itemId, CancellationToken ct);

    /// <summary>Stok+lot no için mevcut lotu bulur, yoksa oluşturur; Lot.Id döner.</summary>
    Task<int> GetOrCreateAsync(int itemId, string lotNo, int? createdById, CancellationToken ct);
}
