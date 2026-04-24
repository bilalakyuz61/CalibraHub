using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IDocumentService
{
    Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesAsync(string? search, string? status, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesByContactAsync(int contactId, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentListItemDto>> GetMovementsByContactAsync(int contactId, int? documentTypeId, DateTime? fromDate, DateTime? toDate, CancellationToken ct);
    Task<DocumentDto?> GetQuoteByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentLineDto>> GetQuoteLinesAsync(int documentId, CancellationToken ct);
    Task<(bool Success, string? Error, DocumentDto? Quote)> SaveQuoteAsync(SaveDocumentRequest request, string? createdBy, CancellationToken ct);
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
