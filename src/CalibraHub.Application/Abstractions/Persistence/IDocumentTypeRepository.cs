using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IDocumentTypeRepository
{
    Task<IReadOnlyCollection<DocumentType>> GetAllAsync(CancellationToken cancellationToken);
    Task<DocumentType?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<DocumentType?> GetByCodeAsync(string code, CancellationToken cancellationToken);
    Task SaveAsync(DocumentType entity, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
