using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IDocumentService
{
    Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesAsync(string? search, string? status, CancellationToken ct);
    Task<DocumentDto?> GetQuoteByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentLineDto>> GetQuoteLinesAsync(int documentId, CancellationToken ct);
    Task<(bool Success, string? Error, DocumentDto? Quote)> SaveQuoteAsync(SaveDocumentRequest request, string? createdBy, CancellationToken ct);
    Task DeleteQuoteAsync(int id, CancellationToken ct);
    Task<(bool Success, string? Error)> ChangeStatusAsync(int id, string newStatus, CancellationToken ct);
    Task<string> GetNextDocumentNumberAsync(CancellationToken ct);
}
