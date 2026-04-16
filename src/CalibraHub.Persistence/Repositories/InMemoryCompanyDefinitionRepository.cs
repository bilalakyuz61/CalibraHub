using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Persistence.Repositories;

public sealed class InMemoryCompanyDefinitionRepository : ICompanyDefinitionRepository
{
    private readonly InMemoryDataStore _dataStore;

    public InMemoryCompanyDefinitionRepository(InMemoryDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public Task<IReadOnlyCollection<CompanyDefinition>> GetAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<CompanyDefinition> result = _dataStore.Companies.Values
            .OrderBy(x => x.Name)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task<CompanyDefinition?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        _dataStore.Companies.TryGetValue(id, out var company);
        return Task.FromResult(company);
    }

    public Task<int> AddAsync(CompanyDefinition company, CancellationToken cancellationToken)
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

    public Task UpdateAsync(CompanyDefinition company, CancellationToken cancellationToken)
    {
        _dataStore.Companies[company.Id] = company;
        return Task.CompletedTask;
    }
}
