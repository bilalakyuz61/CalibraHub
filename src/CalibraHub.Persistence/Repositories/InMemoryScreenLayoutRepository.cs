using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Persistence.Repositories;

public sealed class InMemoryScreenLayoutRepository : IScreenLayoutRepository
{
    private readonly InMemoryDataStore _dataStore;

    public InMemoryScreenLayoutRepository(InMemoryDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public Task<ScreenLayoutDefinition?> GetByScreenCodeAsync(string screenCode, CancellationToken cancellationToken)
    {
        _dataStore.ScreenLayouts.TryGetValue(screenCode, out var definition);
        return Task.FromResult(definition);
    }

    public Task UpsertAsync(ScreenLayoutDefinition definition, CancellationToken cancellationToken)
    {
        _dataStore.ScreenLayouts[definition.ScreenCode] = definition;
        return Task.CompletedTask;
    }
}
