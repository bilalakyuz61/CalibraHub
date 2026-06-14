using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IDocumentService
{
    Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesAsync(string? search, string? status, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesByContactAsync(int contactId, CancellationToken ct);

    /// <summary>Belge tipi kodu ile filtrelenmis liste — orn. "satis_siparisi" siparis ekrani icin.</summary>
    Task<IReadOnlyCollection<DocumentListItemDto>> GetByTypeAsync(string typeCode, string? search, string? status, CancellationToken ct);

    /// <summary>Siparise donusturulebilir teklifleri filtrele — modal listesi icin.</summary>
    Task<IReadOnlyCollection<DocumentListItemDto>> GetConvertibleQuotesAsync(
        DateTime? fromDate, DateTime? toDate, int? contactId, string? search, CancellationToken ct);

    /// <summary>
    /// Secili teklifleri cari bazinda gruplayip her cari icin tek bir siparis (Document, type=satis_siparisi) uretir.
    /// Kaynak teklifin durumu Converted'a geciler, document_source koprusu kayit eklenir.
    /// </summary>
    Task<CreateOrdersFromQuotesResult> CreateOrdersFromQuotesAsync(
        CreateOrdersFromQuotesRequest req, int? createdById, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentListItemDto>> GetMovementsByContactAsync(int contactId, int? documentTypeId, DateTime? fromDate, DateTime? toDate, CancellationToken ct);
    Task<DocumentDto?> GetQuoteByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentLineDto>> GetQuoteLinesAsync(int documentId, CancellationToken ct);
    Task<(bool Success, string? Error, DocumentDto? Quote)> SaveQuoteAsync(SaveDocumentRequest request, int? createdById, CancellationToken ct);
    Task DeleteQuoteAsync(int id, CancellationToken ct);
    Task<(bool Success, string? Error)> ChangeStatusAsync(int id, string newStatus, CancellationToken ct);
    Task<string> GetNextDocumentNumberAsync(CancellationToken ct);

    /// <summary>
    /// Satir revizyonu — eski satirin notes alanini @description ile gunceller,
    /// yeni satir eski'nin birebir kopyasi olarak eklenir (revised_from_id baglantisi
    /// ile). Return: yeni satirin Id'si; parent bulunamazsa null.
    /// Widget (SALES_QUOTE_LINES form) degerlerinin kopyalanmasi controller
    /// tarafinda WidgetService ile yapilir (schema dinamik).
    /// </summary>
    Task<int?> ReviseLineAsync(int parentLineId, string? description, CancellationToken ct);
}
