using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IDocumentRepository
{
    Task<IReadOnlyCollection<Document>> GetAllAsync(string? search, string? status, CancellationToken ct);
    Task<Document?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentLine>> GetLinesAsync(Guid quoteId, CancellationToken ct);
    Task UpsertAsync(Document quote, CancellationToken ct);
    Task SaveLinesAsync(Guid quoteId, IReadOnlyCollection<DocumentLine> lines, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<string> GetNextQuoteNumberAsync(CancellationToken ct);
    Task<IReadOnlyCollection<DocumentLineDetail>> GetLineDetailsAsync(Guid quoteLineId, CancellationToken ct);
    Task SaveLineDetailsAsync(Guid quoteLineId, IReadOnlyCollection<DocumentLineDetail> details, CancellationToken ct);
}
