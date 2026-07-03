namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Sayım "Yansıt" akışı (2026-07-02) — taslak InventoryCountLine girişlerini güncel stok
/// bakiyesiyle karşılaştırıp farkları DocumentLine'a (MovementType=Adjust) atomik olarak yazar.
/// Ham sayım verisi (InventoryCount/InventoryCountLine) IStockDocRepository üzerinden
/// okunur/yazılır — bu arayüz sadece "Yansıt" eylemine özeldir.
/// </summary>
public interface IInventoryCountRepository
{
    /// <summary>
    /// documentId = Document.id (StockDocDto.Id). Optimistic-lock: InventoryCount.Status
    /// zaten Draft değilse (0 satır etkilenirse) InvalidOperationException fırlatır — hiçbir
    /// fark satırı yazılmaz (çift tıklama/network retry idempotency).
    /// Dönen değer: yazılan fark satırı sayısı.
    /// </summary>
    Task<int> ApplyAsync(int documentId, CancellationToken ct);
}
