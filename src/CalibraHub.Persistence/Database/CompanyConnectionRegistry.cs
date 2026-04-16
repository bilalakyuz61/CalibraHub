using System.Collections.Concurrent;
using CalibraHub.Application.Abstractions.Services;

namespace CalibraHub.Persistence.Database;

public sealed class CompanyConnectionRegistry : ICompanyConnectionRegistry
{
    private readonly ConcurrentDictionary<int, string> _map = new();

    public void Set(int companyId, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            _map.TryRemove(companyId, out _);
        else
            _map[companyId] = connectionString;
    }

    public void Remove(int companyId) => _map.TryRemove(companyId, out _);

    public bool TryGet(int companyId, out string connectionString)
    {
        if (_map.TryGetValue(companyId, out var value))
        {
            connectionString = value;
            return true;
        }
        connectionString = string.Empty;
        return false;
    }
}
