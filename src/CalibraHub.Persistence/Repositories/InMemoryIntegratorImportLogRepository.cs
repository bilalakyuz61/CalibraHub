using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Persistence.Repositories;

public sealed class InMemoryIntegratorImportLogRepository : IIntegratorImportLogRepository
{
    private readonly InMemoryDataStore _dataStore;

    public InMemoryIntegratorImportLogRepository(InMemoryDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public Task WriteAsync(IntegratorImportLogWriteRequest request, CancellationToken cancellationToken)
    {
        var companyName = request.CompanyId.HasValue &&
                          _dataStore.Companies.TryGetValue(request.CompanyId.Value, out var company)
            ? company.Name
            : "-";

        var entry = new IntegratorImportLogEntryDto(
            request.OccurredAt ?? DateTime.Now,
            request.IntegratorSettingsId,
            request.CompanyId,
            companyName,
            string.IsNullOrWhiteSpace(request.IntegratorName) ? "Calibra" : request.IntegratorName.Trim(),
            string.IsNullOrWhiteSpace(request.Level) ? "Info" : request.Level.Trim(),
            string.IsNullOrWhiteSpace(request.Message) ? "Log kaydi" : request.Message.Trim(),
            request.ImportedCount,
            request.SkippedCount,
            "INMEMORY");

        _dataStore.IntegratorImportLogs[Guid.NewGuid()] = entry;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<IntegratorImportLogEntryDto>> GetRecentAsync(
        int take,
        CancellationToken cancellationToken)
    {
        if (take <= 0)
        {
            return Task.FromResult<IReadOnlyCollection<IntegratorImportLogEntryDto>>(Array.Empty<IntegratorImportLogEntryDto>());
        }

        IReadOnlyCollection<IntegratorImportLogEntryDto> result = _dataStore.IntegratorImportLogs.Values
            .OrderByDescending(x => x.OccurredAt)
            .Take(take)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task CleanupExpiredAsync(
        int integratorSettingsId,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.Now.AddDays(-Math.Clamp(retentionDays, 1, 3650));
        var expiredIds = _dataStore.IntegratorImportLogs
            .Where(x =>
                x.Value.IntegratorSettingsId == integratorSettingsId &&
                x.Value.OccurredAt < cutoff)
            .Select(x => x.Key)
            .ToArray();

        foreach (var expiredId in expiredIds)
        {
            _dataStore.IntegratorImportLogs.TryRemove(expiredId, out _);
        }

        return Task.CompletedTask;
    }
}
