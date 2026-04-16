using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Persistence.Repositories;

public sealed class InMemoryDepartmentRepository : IDepartmentRepository
{
    private readonly InMemoryDataStore _dataStore;

    public InMemoryDepartmentRepository(InMemoryDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public Task<IReadOnlyCollection<Department>> GetAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Department> result = _dataStore.Departments.Values
            .OrderBy(x => x.Code)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task AddAsync(Department department, CancellationToken cancellationToken)
    {
        _dataStore.Departments[department.Id] = department;
        return Task.CompletedTask;
    }
}
