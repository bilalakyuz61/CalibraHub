using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Persistence.Repositories;

public sealed class InMemoryIntegratorSettingsRepository : IIntegratorSettingsRepository
{
    private readonly InMemoryDataStore _dataStore;

    public InMemoryIntegratorSettingsRepository(InMemoryDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public Task<IReadOnlyCollection<IntegratorSettings>> GetAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<IntegratorSettings> result = _dataStore.IntegratorSettings.Values.ToArray();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyCollection<IntegratorSettings>> GetActiveAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<IntegratorSettings> result = _dataStore.IntegratorSettings.Values
            .Where(x => x.IsActive)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task<IntegratorSettings?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        _dataStore.IntegratorSettings.TryGetValue(id, out var settings);
        return Task.FromResult(settings);
    }

    public Task<IntegratorSettings?> GetByCompanyIdAsync(int companyId, CancellationToken cancellationToken)
    {
        var settings = _dataStore.IntegratorSettings.Values.FirstOrDefault(x => x.CompanyId == companyId);
        return Task.FromResult(settings);
    }

    public Task<int> AddAsync(IntegratorSettings settings, CancellationToken cancellationToken)
    {
        var newId = _dataStore.IntegratorSettings.IsEmpty ? 1 : _dataStore.IntegratorSettings.Keys.Max() + 1;
        var withId = new IntegratorSettings
        {
            Id = newId,
            CompanyId = settings.CompanyId,
            Provider = settings.Provider,
            Name = settings.Name,
            BaseUrl = settings.BaseUrl,
            CompanyTaxNumber = settings.CompanyTaxNumber,
            Username = settings.Username,
            Secret = settings.Secret,
            AppStr = settings.AppStr,
            Source = settings.Source,
            AppVersion = settings.AppVersion,
            CreatedAt = settings.CreatedAt
        };
        withId.UpdatePollingInterval(settings.PollingIntervalSeconds);
        withId.UpdateMaxRecordsPerPull(settings.MaxRecordsPerPull);
        withId.UpdateLogRetentionDays(settings.LogRetentionDays);
        withId.UpdateTimeoutSeconds(settings.TimeoutSeconds);
        withId.UpdateLookbackDays(settings.LookbackDays);
        withId.ConfigureIncludeReceivedDocumentsInPull(settings.IncludeReceivedDocumentsInPull);
        withId.ConfigureDownloadedDocumentReceipt(settings.MarkDownloadedDocumentsAsReceived);
        withId.ConfigureIssuedDocumentPull(settings.IncludeIssuedEInvoicesInPull, settings.IncludeIssuedEArchivesInPull, settings.IncludeIssuedEDispatchesInPull);
        withId.ConfigureScheduleEnabled(settings.ScheduleEnabled);
        if (!settings.IsActive) withId.Deactivate();
        _dataStore.IntegratorSettings[newId] = withId;
        return Task.FromResult(newId);
    }

    public Task UpdateAsync(IntegratorSettings settings, CancellationToken cancellationToken)
    {
        // Sifre bos geldiyse mevcut saklanan deger korunur
        if (string.IsNullOrWhiteSpace(settings.Secret) &&
            _dataStore.IntegratorSettings.TryGetValue(settings.Id, out var existing) &&
            !string.IsNullOrWhiteSpace(existing.Secret))
        {
            var incoming = settings;
            settings = new IntegratorSettings
            {
                Id = incoming.Id,
                CompanyId = incoming.CompanyId,
                Provider = incoming.Provider,
                Name = incoming.Name,
                BaseUrl = incoming.BaseUrl,
                CompanyTaxNumber = incoming.CompanyTaxNumber,
                Username = incoming.Username,
                Secret = existing.Secret,
                AppStr = incoming.AppStr,
                Source = incoming.Source,
                AppVersion = incoming.AppVersion,
                CreatedAt = incoming.CreatedAt
            };
            settings.UpdatePollingInterval(incoming.PollingIntervalSeconds);
            settings.UpdateMaxRecordsPerPull(incoming.MaxRecordsPerPull);
            settings.UpdateLogRetentionDays(incoming.LogRetentionDays);
            settings.UpdateTimeoutSeconds(incoming.TimeoutSeconds);
            settings.UpdateLookbackDays(incoming.LookbackDays);
            settings.ConfigureIncludeReceivedDocumentsInPull(incoming.IncludeReceivedDocumentsInPull);
            settings.ConfigureDownloadedDocumentReceipt(incoming.MarkDownloadedDocumentsAsReceived);
            settings.ConfigureIssuedDocumentPull(incoming.IncludeIssuedEInvoicesInPull, incoming.IncludeIssuedEArchivesInPull, incoming.IncludeIssuedEDispatchesInPull);
            settings.ConfigureScheduleEnabled(incoming.ScheduleEnabled);
            if (!incoming.IsActive) settings.Deactivate();
        }

        _dataStore.IntegratorSettings[settings.Id] = settings;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (!_dataStore.IntegratorSettings.ContainsKey(id))
        {
            throw new ArgumentException("Id bulunamadi.", nameof(id));
        }

        _dataStore.IntegratorSettings.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
