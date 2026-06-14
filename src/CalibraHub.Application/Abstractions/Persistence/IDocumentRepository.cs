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
    Task<string> GetNextDocumentNumberAsync(CancellationToken ct);
    Task<IReadOnlyCollection<DocumentLineDetail>> GetLineDetailsAsync(int documentLineId, CancellationToken ct);
    Task SaveLineDetailsAsync(int documentLineId, IReadOnlyCollection<DocumentLineDetail> details, CancellationToken ct);

    /// <summary>
    /// İhtiyaç Kaydı satırının karşılama miktarlarını günceller ve FulfillmentStatus'ı yeniden hesaplar.
    /// fulfilledFromStock / fulfilledByPurchase kümülatif DELTA değil, yeni toplam değerdir.
    /// </summary>
    Task UpdateLineFulfillmentAsync(int lineId, decimal fulfilledFromStock, decimal fulfilledByPurchase, CancellationToken ct);

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
}
