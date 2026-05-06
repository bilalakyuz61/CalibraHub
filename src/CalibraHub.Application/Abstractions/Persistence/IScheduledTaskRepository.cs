using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IScheduledTaskRepository
{
    Task<IReadOnlyList<ScheduledTask>> GetAllAsync(CancellationToken cancellationToken);
    Task<ScheduledTask?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<ScheduledTask?> GetByNameAsync(string name, CancellationToken cancellationToken);

    /// <summary>Polling: enabled + next_run_at <= NOW olan gorevleri doner.</summary>
    Task<IReadOnlyList<ScheduledTask>> GetDueTasksAsync(DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Name bazinda UPSERT — meta alanlari (Description, Schedule*) ayarlar.
    /// Worker startup'inda built-in tasklarin tanimlanmasi icin kullanilir.
    /// LastRun/NextRun alanlarina DOKUNMAZ.
    /// </summary>
    Task UpsertRegistrationAsync(ScheduledTask task, CancellationToken cancellationToken);

    /// <summary>Kullanici tarafindan olusturulan yeni task (ADD veya FULL UPDATE).</summary>
    Task<int> SaveAsync(ScheduledTask task, CancellationToken cancellationToken);

    /// <summary>Run raporu — LastRunAt/Status/Message/DurationMs/NextRunAt gunceller.</summary>
    Task ReportRunAsync(int taskId, int status, string? message, int? durationMs, DateTime? nextRunAt, CancellationToken cancellationToken);

    /// <summary>IsRunning flag'i set/clear eder — concurrent dispatch engeller.</summary>
    Task<bool> TryAcquireLockAsync(int taskId, CancellationToken cancellationToken);
    Task ReleaseLockAsync(int taskId, CancellationToken cancellationToken);

    Task SetEnabledAsync(int taskId, bool enabled, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
