using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IDocumentService
{
    Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesAsync(string? search, string? status, CancellationToken ct);
    Task<DocumentDto?> GetQuoteByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentLineDto>> GetQuoteLinesAsync(Guid quoteId, CancellationToken ct);
    Task<(bool Success, string? Error, DocumentDto? Quote)> SaveQuoteAsync(SaveDocumentRequest request, string? createdBy, CancellationToken ct);
    Task DeleteQuoteAsync(Guid id, CancellationToken ct);
    Task<(bool Success, string? Error)> ChangeStatusAsync(Guid id, string newStatus, CancellationToken ct);
    Task<string> GetNextQuoteNumberAsync(CancellationToken ct);
}
