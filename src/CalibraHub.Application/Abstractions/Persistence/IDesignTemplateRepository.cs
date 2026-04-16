using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IDesignTemplateRepository
{
    Task<IReadOnlyCollection<DesignTemplate>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<DesignTemplate>> GetByTypeAsync(string type, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<DesignTemplate>> GetBySubTypeAsync(string subType, CancellationToken cancellationToken);
    Task<DesignTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(DesignTemplate template, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
