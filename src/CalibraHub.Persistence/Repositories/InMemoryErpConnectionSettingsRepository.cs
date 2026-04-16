using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Persistence.Repositories;

public sealed class InMemoryErpConnectionSettingsRepository : IErpConnectionSettingsRepository
{
    private readonly InMemoryDataStore _dataStore;

    public InMemoryErpConnectionSettingsRepository(InMemoryDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public Task<IReadOnlyCollection<ErpConnectionSettings>> GetAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<ErpConnectionSettings> result = _dataStore.ErpConnectionSettings.Values
            .OrderBy(x => x.Company)
            .ThenBy(x => x.Business)
            .ThenBy(x => x.Branch)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task<ErpConnectionSettings?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _dataStore.ErpConnectionSettings.TryGetValue(id, out var settings);
        return Task.FromResult(settings);
    }

    public Task AddAsync(ErpConnectionSettings settings, CancellationToken cancellationToken)
    {
        _dataStore.ErpConnectionSettings[settings.Id] = settings;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ErpConnectionSettings settings, CancellationToken cancellationToken)
    {
        _dataStore.ErpConnectionSettings[settings.Id] = settings;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!_dataStore.ErpConnectionSettings.ContainsKey(id))
        {
            throw new ArgumentException("Id bulunamadi.", nameof(id));
        }

        _dataStore.ErpConnectionSettings.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
