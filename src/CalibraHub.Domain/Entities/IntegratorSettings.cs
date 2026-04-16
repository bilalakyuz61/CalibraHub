using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class IntegratorSettings
{
    public int Id { get; init; }
    public int CompanyId { get; init; }
    public IntegratorProvider Provider { get; init; }
    public required string Name { get; init; }
    public required string BaseUrl { get; init; }
    public required string CompanyTaxNumber { get; init; }
    public required string Username { get; init; }
    public required string Secret { get; init; }
    public string? AppStr { get; init; }
    public string? Source { get; init; }
    public string? AppVersion { get; init; }
    public int TimeoutSeconds { get; private set; } = 30;
    public int LookbackDays { get; private set; } = 30;
    public int PollingIntervalSeconds { get; private set; } = 120;
    public int MaxRecordsPerPull { get; private set; } = 200;
    public int LogRetentionDays { get; private set; } = 30;
    public bool IncludeReceivedDocumentsInPull { get; private set; }
    public bool MarkDownloadedDocumentsAsReceived { get; private set; }
    public bool IncludeIssuedEInvoicesInPull { get; private set; }
    public bool IncludeIssuedEArchivesInPull { get; private set; }
    public bool IncludeIssuedEDispatchesInPull { get; private set; }
    public bool ScheduleEnabled { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; private set; } = DateTime.Now;

    public void UpdateTimeoutSeconds(int timeoutSeconds)
    {
        TimeoutSeconds = timeoutSeconds;
        UpdatedAt = DateTime.Now;
    }

    public void UpdateLookbackDays(int lookbackDays)
    {
        LookbackDays = lookbackDays;
        UpdatedAt = DateTime.Now;
    }

    public void UpdatePollingInterval(int pollingIntervalSeconds)
    {
        PollingIntervalSeconds = pollingIntervalSeconds;
        UpdatedAt = DateTime.Now;
    }

    public void UpdateMaxRecordsPerPull(int maxRecordsPerPull)
    {
        MaxRecordsPerPull = maxRecordsPerPull;
        UpdatedAt = DateTime.Now;
    }

    public void UpdateLogRetentionDays(int logRetentionDays)
    {
        LogRetentionDays = logRetentionDays;
        UpdatedAt = DateTime.Now;
    }

    public void ConfigureDownloadedDocumentReceipt(bool markAsReceived)
    {
        MarkDownloadedDocumentsAsReceived = markAsReceived;
        UpdatedAt = DateTime.Now;
    }

    public void ConfigureIncludeReceivedDocumentsInPull(bool includeReceivedDocumentsInPull)
    {
        IncludeReceivedDocumentsInPull = includeReceivedDocumentsInPull;
        UpdatedAt = DateTime.Now;
    }

    public void ConfigureIssuedDocumentPull(
        bool includeIssuedEInvoicesInPull,
        bool includeIssuedEArchivesInPull,
        bool includeIssuedEDispatchesInPull)
    {
        IncludeIssuedEInvoicesInPull = includeIssuedEInvoicesInPull;
        IncludeIssuedEArchivesInPull = includeIssuedEArchivesInPull;
        IncludeIssuedEDispatchesInPull = includeIssuedEDispatchesInPull;
        UpdatedAt = DateTime.Now;
    }

    public void ConfigureScheduleEnabled(bool scheduleEnabled)
    {
        ScheduleEnabled = scheduleEnabled;
        UpdatedAt = DateTime.Now;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.Now;
    }
}
