using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IScheduledTaskRepository
{
    Task<IReadOnlyList<ScheduledTask>> GetAllAsync(CancellationToken cancellationToken);
    Task<ScheduledTask?> GetByCodeAsync(string code, CancellationToken cancellationToken);
    Task<ScheduledTask?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Polling: enabled + next_run_at <= NOW olan gorevleri doner.</summary>
    Task<IReadOnlyList<ScheduledTask>> GetDueTasksAsync(DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Code bazinda UPSERT — meta alanlari (Name, Description, Schedule*) ayarlar.
    /// Worker startup'inda cagrilir. LastRun/NextRun alanlarina DOKUNMAZ.
    /// </summary>
    Task UpsertRegistrationAsync(ScheduledTask task, CancellationToken cancellationToken);

    /// <summary>Kullanici tarafindan olusturulan yeni task (ADD veya FULL UPDATE).</summary>
    Task<int> SaveAsync(ScheduledTask task, CancellationToken cancellationToken);

    /// <summary>Run raporu — LastRunAt/Status/Message/DurationMs/NextRunAt gunceller.</summary>
    Task ReportRunAsync(string code, int status, string? message, int? durationMs, DateTime? nextRunAt, CancellationToken cancellationToken);

    /// <summary>IsRunning flag'i set/clear eder — concurrent dispatch engeller.</summary>
    Task<bool> TryAcquireLockAsync(string code, CancellationToken cancellationToken);
    Task ReleaseLockAsync(string code, CancellationToken cancellationToken);

    Task SetEnabledAsync(string code, bool enabled, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
