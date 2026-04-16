using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Persistence.Repositories;

public sealed class InMemorySmtpProfileRepository : ISmtpProfileRepository
{
    private readonly InMemoryDataStore _dataStore;

    public InMemorySmtpProfileRepository(InMemoryDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public Task<IReadOnlyCollection<SmtpProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<SmtpProfile> result = _dataStore.SmtpProfiles.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task<SmtpProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        _dataStore.SmtpProfiles.TryGetValue(id, out var profile);
        return Task.FromResult(profile);
    }

    public Task AddAsync(SmtpProfile profile, CancellationToken cancellationToken)
    {
        _dataStore.SmtpProfiles[profile.Id] = profile;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(SmtpProfile profile, CancellationToken cancellationToken)
    {
        _dataStore.SmtpProfiles[profile.Id] = profile;
        return Task.CompletedTask;
    }
}
