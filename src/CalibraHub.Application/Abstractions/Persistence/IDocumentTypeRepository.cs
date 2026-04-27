using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IDocumentTypeRepository
{
    Task<IReadOnlyCollection<DocumentType>> GetAllAsync(CancellationToken cancellationToken);
    Task<DocumentType?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<DocumentType?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>INSERT veya UPDATE. Yeni Id'yi doner (IDENTITY).</summary>
    Task<int> SaveAsync(DocumentType entity, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
