using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IDocumentRepository
{
    Task<IReadOnlyCollection<Document>> GetAllAsync(string? search, string? status, CancellationToken ct);
    Task<Document?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentLine>> GetLinesAsync(int documentId, CancellationToken ct);

    /// <summary>INSERT veya UPDATE. Yeni Id'yi doner (IDENTITY).</summary>
    Task<int> UpsertAsync(Document document, CancellationToken ct);

    Task SaveLinesAsync(int documentId, IReadOnlyCollection<DocumentLine> lines, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task<string> GetNextDocumentNumberAsync(CancellationToken ct);
    Task<IReadOnlyCollection<DocumentLineDetail>> GetLineDetailsAsync(int documentLineId, CancellationToken ct);
    Task SaveLineDetailsAsync(int documentLineId, IReadOnlyCollection<DocumentLineDetail> details, CancellationToken ct);
}
