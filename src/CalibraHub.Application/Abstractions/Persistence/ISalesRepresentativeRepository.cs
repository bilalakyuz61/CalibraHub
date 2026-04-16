using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ISalesRepresentativeRepository
{
    Task<IReadOnlyCollection<SalesRepresentative>> GetAllAsync(CancellationToken cancellationToken);
    Task<SalesRepresentative?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<int> AddAsync(SalesRepresentative entity, CancellationToken cancellationToken);
    Task UpdateAsync(SalesRepresentative entity, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
