using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ISalesQuoteRepository
{
    Task<IReadOnlyCollection<SalesQuote>> GetAllAsync(string? search, string? status, CancellationToken ct);
    Task<SalesQuote?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyCollection<SalesQuoteLine>> GetLinesAsync(Guid quoteId, CancellationToken ct);
    Task UpsertAsync(SalesQuote quote, CancellationToken ct);
    Task SaveLinesAsync(Guid quoteId, IReadOnlyCollection<SalesQuoteLine> lines, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<string> GetNextQuoteNumberAsync(CancellationToken ct);
    Task<IReadOnlyCollection<SalesQuoteLineDetail>> GetLineDetailsAsync(Guid quoteLineId, CancellationToken ct);
    Task SaveLineDetailsAsync(Guid quoteLineId, IReadOnlyCollection<SalesQuoteLineDetail> details, CancellationToken ct);
}
