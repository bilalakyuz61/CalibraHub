using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IDocumentRepository
{
    Task<IReadOnlyCollection<Document>> GetAllAsync(string? search, string? status, CancellationToken ct);

    /// <summary>
    /// Belge tipi kodu = @typeCode (orn. "satis_teklifi" / "satis_siparisi") olan belgeleri filtrele.
    /// Status / search / tarih araligi opsiyoneldir; CalibraHub teklif vs. siparis listesinde kullanilir.
    /// </summary>
    Task<IReadOnlyCollection<Document>> GetByTypeAsync(string typeCode, string? search, string? status, CancellationToken ct);

    /// <summary>
    /// Siparise donusturulebilir teklifler — Status=Approved, type=satis_teklifi,
    /// daha onceden document_source koprusunde kaynak olarak yer almamis. Opsiyonel
    /// filtreler: tarih araligi, cari, belge no.
    /// </summary>
    Task<IReadOnlyCollection<Document>> GetConvertibleQuotesAsync(
        DateTime? fromDate, DateTime? toDate, int? contactId, string? search, CancellationToken ct);

    Task<Document?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentLine>> GetLinesAsync(int documentId, CancellationToken ct);

    /// <summary>INSERT veya UPDATE. Yeni Id'yi doner (IDENTITY).</summary>
    Task<int> UpsertAsync(Document document, CancellationToken ct);

    Task SaveLinesAsync(int documentId, IReadOnlyCollection<DocumentLine> lines, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>Sadece [status] kolonunu günceller — onay başlatıldığında, onaylandığında vb.</summary>
    Task UpdateStatusAsync(int id, string status, CancellationToken ct);
    Task<string> GetNextDocumentNumberAsync(CancellationToken ct);
    Task<IReadOnlyCollection<DocumentLineDetail>> GetLineDetailsAsync(int documentLineId, CancellationToken ct);
    Task SaveLineDetailsAsync(int documentLineId, IReadOnlyCollection<DocumentLineDetail> details, CancellationToken ct);

    // ── Kit snapshot (Faz 2) ─────────────────────────────────────────
    /// <summary>Verilen item id'lerinden AKTIF kit olanlarin icerigini (VersionNo + bilesenler)
    /// doner. Kit olmayan / kit tanimi olmayan id'ler sonuca girmez.</summary>
    Task<IReadOnlyCollection<KitSnapshotSourceDto>> GetActiveKitContentsAsync(
        IEnumerable<int> kitItemIds, CancellationToken ct);

    /// <summary>Verilen belge satiri id'leri icin mevcut kit snapshot anahtarlari (LineId, KitItemId).
    /// Freeze: ayni satirda ayni kit zaten snapshot'landiysa yeniden alinmaz (kit revizyonu gecmis
    /// belgeyi etkilemez). Satirin item'i baska bir kite degistiyse yeniden snapshot alinir.</summary>
    Task<IReadOnlyCollection<(int LineId, int KitItemId)>> GetExistingKitSnapshotsAsync(
        IEnumerable<int> documentLineIds, CancellationToken ct);

    /// <summary>Bir kit satirinin snapshot'ini (DELETE + INSERT) yazar — o anki aktif kit icerigi dondurulur.</summary>
    Task ReplaceKitSnapshotAsync(int documentLineId, int kitItemId, int versionNo,
        IReadOnlyCollection<KitSnapshotComponentDto> components, CancellationToken ct);

    /// <summary>Bir kit satirinin dondurulmus icerigini (enriched: bilesen kod/ad) doner. Faz 3 patlatma + grid gosterimi.</summary>
    Task<IReadOnlyCollection<DocumentLineKitComponent>> GetKitSnapshotAsync(int documentLineId, CancellationToken ct);

    /// <summary>
    /// İhtiyaç Kaydı satırının karşılama miktarlarını günceller ve FulfillmentStatus'ı yeniden hesaplar.
    /// fulfilledFromStock / fulfilledByPurchase kümülatif DELTA değil, yeni toplam değerdir.
    /// </summary>
    Task UpdateLineFulfillmentAsync(int lineId, decimal fulfilledFromStock, decimal fulfilledByPurchase, CancellationToken ct);

    /// <summary>
    /// Belgenin satırlarına SourceLineId ile referans veren (AKTİF belgelerdeki) türetilmiş
    /// satır agregatları: kaynakLineId → (türetilmiş satır sayısı, toplam miktar).
    /// Bağlantı bütünlüğü guard'ları için: referanslı kaynak kalem silinemez, miktarı
    /// türetilmiş toplamın altına düşürülemez (teklif→sipariş vb. zincirler).
    /// </summary>
    Task<IReadOnlyDictionary<int, (int Count, decimal QtySum)>> GetDerivedLineAggregatesAsync(int documentId, CancellationToken ct);

    /// <summary>
    /// Bir satiri revize et — atomik olarak:
    ///   1) Eski satirin notes = @description (revize gerekcesi / eski halin anlatimi)
    ///   2) Yeni satir: eski satirin birebir kopyasi + revised_from_id = parentLineId
    ///   3) Kombinasyon detaylari da yeni satira kopyalanir (cascade preserve).
    /// Widget degerleri (WidgetService) controller tarafinda ayrica kopyalanir.
    /// Return: yeni satirin Id'si (kullanici arayuzu ve widget kopyasi icin).
    /// Parent bulunamazsa null doner.
    /// </summary>
    Task<int?> ReviseLineAsync(int parentLineId, string? description, CancellationToken ct);

    /// <summary>
    /// Append-only: DocumentLine'a TEK yeni stok hareketi satırı ekler (LineNo = mevcut
    /// max+1, UPDLOCK+HOLDLOCK ile concurrent-safe). SaveLinesAsync'in upsert-all/replace
    /// davranışından FARKLIDIR — mevcut satırları etkilemez, sadece INSERT yapar. WorkOrder
    /// üretim olayları ve Sayım "Yansıt" fark satırları için kullanılır. Yeni satırın Id'sini döner.
    /// </summary>
    Task<int> AppendStockLineAsync(int documentId, DocumentLine line, CancellationToken ct);
}
