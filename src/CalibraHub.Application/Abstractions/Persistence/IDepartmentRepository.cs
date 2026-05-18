using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IDepartmentRepository
{
    Task<IReadOnlyCollection<Department>> GetAllAsync(CancellationToken cancellationToken);
    Task<Department?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<int> AddAsync(Department department, CancellationToken cancellationToken);
    Task UpdateAsync(Department department, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
