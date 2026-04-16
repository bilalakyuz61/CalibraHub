using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface ISalesQuoteService
{
    Task<IReadOnlyCollection<SalesQuoteListItemDto>> GetQuotesAsync(string? search, string? status, CancellationToken ct);
    Task<SalesQuoteDto?> GetQuoteByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyCollection<SalesQuoteLineDto>> GetQuoteLinesAsync(Guid quoteId, CancellationToken ct);
    Task<(bool Success, string? Error, SalesQuoteDto? Quote)> SaveQuoteAsync(SaveSalesQuoteRequest request, string? createdBy, CancellationToken ct);
    Task DeleteQuoteAsync(Guid id, CancellationToken ct);
    Task<(bool Success, string? Error)> ChangeStatusAsync(Guid id, string newStatus, CancellationToken ct);
    Task<string> GetNextQuoteNumberAsync(CancellationToken ct);
}
