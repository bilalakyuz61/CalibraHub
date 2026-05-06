using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IScheduledTaskRunRepository
{
    /// <summary>Yeni run kaydi olusturur, id doner.</summary>
    Task<int> CreateAsync(ScheduledTaskRun run, CancellationToken cancellationToken);

    /// <summary>
    /// Varolan run'i completed olarak isaretler — status + message + duration + opsiyonel
    /// executedCommand (calistirilan SQL/HTTP komutu). executedCommand null ise mevcut
    /// degeri korunur (yeniden CompleteAsync cagrilarinda overwrite icin yeni deger gonder).
    /// </summary>
    Task CompleteAsync(int runId, int status, string? message, int durationMs, string? executedCommand, CancellationToken cancellationToken);

    /// <summary>Belirli gorevin son N calistirma gecmisini doner (en yeni en ustte).</summary>
    Task<IReadOnlyList<ScheduledTaskRun>> GetRecentByTaskIdAsync(int taskId, int limit, CancellationToken cancellationToken);

    /// <summary>Eski run'lari temizler (ornek son 30 gunden eski).</summary>
    Task PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken);
}
