using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Persistence.Repositories;

public sealed class InMemoryDepartmentRepository : IDepartmentRepository
{
    private readonly InMemoryDataStore _dataStore;
    private int _nextId = 100; // dev seed icin 1,2 zaten alinmis

    public InMemoryDepartmentRepository(InMemoryDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public Task<IReadOnlyCollection<Department>> GetAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Department> result = _dataStore.Departments.Values
            .OrderBy(x => x.Name)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task<Department?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        _dataStore.Departments.TryGetValue(id, out var dept);
        return Task.FromResult<Department?>(dept);
    }

    public Task<int> AddAsync(Department department, CancellationToken cancellationToken)
    {
        var id = System.Threading.Interlocked.Increment(ref _nextId);
        var withId = new Department
        {
            Id = id,
            CompanyId = department.CompanyId,
            Name = department.Name,
            ParentDepartmentId = department.ParentDepartmentId,
        };
        if (!department.IsActive) withId.Deactivate();
        _dataStore.Departments[id] = withId;
        return Task.FromResult(id);
    }

    public Task UpdateAsync(Department department, CancellationToken cancellationToken)
    {
        _dataStore.Departments[department.Id] = department;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        _dataStore.Departments.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
