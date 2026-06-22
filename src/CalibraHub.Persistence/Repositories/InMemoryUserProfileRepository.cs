using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Persistence.Repositories;

public sealed class InMemoryUserProfileRepository : IUserProfileRepository
{
    private readonly InMemoryDataStore _dataStore;

    public InMemoryUserProfileRepository(InMemoryDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public Task<IReadOnlyCollection<UserProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<UserProfile> result = _dataStore.Users.Values
            .OrderBy(x => x.FullName)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task<UserProfile?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var user = _dataStore.Users.Values
            .FirstOrDefault(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(user);
    }

    public Task<UserProfile?> GetByEmailAndCompanyIdAsync(
        string email,
        int companyId,
        CancellationToken cancellationToken)
    {
        var user = _dataStore.Users.Values
            .FirstOrDefault(x =>
                x.CompanyId == companyId &&
                string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(user);
    }

    public Task<UserProfile?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        _dataStore.Users.TryGetValue(id, out var user);
        return Task.FromResult(user);
    }

    public Task AddAsync(UserProfile userProfile, CancellationToken cancellationToken)
    {
        _dataStore.Users[userProfile.Id] = userProfile;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(UserProfile userProfile, CancellationToken cancellationToken)
    {
        _dataStore.Users[userProfile.Id] = userProfile;
        return Task.CompletedTask;
    }

    // In-memory: token'ları user dictionary'ye ayrıca saklamak yerine basit bir dict kullan
    private readonly Dictionary<int, (string Token, DateTime Expiry)> _resetTokens = new();

    public Task SetResetTokenAsync(int userId, string token, DateTime expiry, CancellationToken ct)
    {
        _resetTokens[userId] = (token, expiry);
        return Task.CompletedTask;
    }

    public Task<UserProfile?> GetByResetTokenAsync(string token, CancellationToken ct)
    {
        var entry = _resetTokens.FirstOrDefault(kv =>
            kv.Value.Token == token && kv.Value.Expiry > DateTime.UtcNow);
        if (entry.Key == 0) return Task.FromResult<UserProfile?>(null);
        _dataStore.Users.TryGetValue(entry.Key, out var user);
        return Task.FromResult<UserProfile?>(user?.IsActive == true ? user : null);
    }

    public Task ClearResetTokenAsync(int userId, CancellationToken ct)
    {
        _resetTokens.Remove(userId);
        return Task.CompletedTask;
    }
}
