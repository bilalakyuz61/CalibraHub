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

    /// <summary>
    /// Sayım bağlantısız bakiye sıfırlama — sayım deposundaki TÜM canlı bakiyeleri
    /// (sayım kalemlerinden bağımsız) sıfırlayan Adjust satırlarını bu belgeye yazar.
    /// Doğal idempotent: ikinci çağrıda bakiyeler zaten 0 olduğundan satır yazılmaz.
    /// Sayım deposu seçilmemişse InvalidOperationException. Dönen değer: yazılan satır sayısı.
    /// </summary>
    Task<int> ZeroLocationBalancesAsync(int documentId, CancellationToken ct);

    /// <summary>
    /// Sayılmayan stokların sıfırlanması — sayım deposunda bakiyesi olup sayım kalemlerinde
    /// YER ALMAYAN (ItemId + ConfigId eşleşmesi) stoklar için sıfırlama Adjust satırları yazar.
    /// Doğal idempotent. Dönen değer: yazılan satır sayısı.
    /// </summary>
    Task<int> ZeroUncountedAsync(int documentId, CancellationToken ct);

    /// <summary>
    /// InventoryCount.Status döndürür (0=Draft, 1=Applied, 2=Cancelled). Belge bir sayım
    /// değilse null. Silme/düzenleme guard'ları için — Applied sayım immutable olmalı
    /// (yansıtılan fark satırları belgeye bağlı; silinirse bakiye sessizce geri döner).
    /// </summary>
    Task<byte?> GetStatusAsync(int documentId, CancellationToken ct);

    /// <summary>
    /// Yansıtılmış (Status=1) sayımların DocumentId set'ini toplu döndürür — board/liste
    /// ekranında "Yansıtıldı" rozeti + Sil'i gizlemek için (N+1 önlemi).
    /// </summary>
    Task<IReadOnlySet<int>> GetAppliedDocumentIdsAsync(CancellationToken ct);

    /// <summary>
    /// Yansıtma iptali (unpost) — yansıtılmış sayımı taslağa geri döndürür. Optimistic-lock:
    /// yalnızca Status=1 iken çalışır (aksi halde InvalidOperationException). Bu sayım fişinin
    /// ürettiği tüm stok hareketlerini (MovementType=4 DocumentLine — Yansıt farkları + İşlemler
    /// sekmesi sıfırlamaları) siler ve Status'u Draft (0) yapar. Sonrasında fiş serbestçe
    /// düzenlenebilir/silinebilir. Dönen değer: geri alınan (silinen) hareket satırı sayısı.
    /// </summary>
    Task<int> RevertAsync(int documentId, CancellationToken ct);
}
