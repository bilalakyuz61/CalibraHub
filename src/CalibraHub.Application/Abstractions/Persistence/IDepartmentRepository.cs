using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IDepartmentRepository
{
    Task<IReadOnlyCollection<Department>> GetAllAsync(CancellationToken cancellationToken);
    Task AddAsync(Department department, CancellationToken cancellationToken);
}
