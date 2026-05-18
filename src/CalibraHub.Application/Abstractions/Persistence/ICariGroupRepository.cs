using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ICariGroupRepository
{
    Task<IReadOnlyCollection<CariGroup>> GetAllAsync(CancellationToken cancellationToken);
    Task<CariGroup?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<int> AddAsync(CariGroup entity, CancellationToken cancellationToken);
    Task UpdateAsync(CariGroup entity, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
