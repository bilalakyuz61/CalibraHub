using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Persistence.Repositories;

public sealed class InMemoryCompanyRepository : ICompanyRepository
{
    private readonly InMemoryDataStore _dataStore;

    public InMemoryCompanyRepository(InMemoryDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public Task<IReadOnlyCollection<Company>> GetAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Company> result = _dataStore.Companies.Values
            .OrderBy(x => x.Name)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task<Company?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        _dataStore.Companies.TryGetValue(id, out var company);
        return Task.FromResult(company);
    }

    public Task<int> AddAsync(Company company, CancellationToken cancellationToken)
    {
        // For in-memory, assign next available id if not set
        if (company.Id == 0)
        {
            var nextId = _dataStore.Companies.Count > 0 ? _dataStore.Companies.Keys.Max() + 1 : 1;
            company.Id = nextId;
        }
        _dataStore.Companies[company.Id] = company;
        return Task.FromResult(company.Id);
    }

    public Task UpdateAsync(Company company, CancellationToken cancellationToken)
    {
        _dataStore.Companies[company.Id] = company;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        _dataStore.Companies.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
