using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IScheduledTaskRunRepository
{
    /// <summary>Yeni run kaydi olusturur, id doner.</summary>
    Task<int> CreateAsync(ScheduledTaskRun run, CancellationToken cancellationToken);

    /// <summary>Varolan run'i completed olarak isaretler (status + message + duration set).</summary>
    Task CompleteAsync(int runId, int status, string? message, int durationMs, CancellationToken cancellationToken);

    /// <summary>Belirli gorevin son N calistirma gecmisini doner (en yeni en ustte).</summary>
    Task<IReadOnlyList<ScheduledTaskRun>> GetRecentByTaskAsync(int taskId, int limit, CancellationToken cancellationToken);

    /// <summary>Bir run'i kod ile arar — manual trigger sirasinda kullanilir.</summary>
    Task<IReadOnlyList<ScheduledTaskRun>> GetRecentByCodeAsync(string code, int limit, CancellationToken cancellationToken);

    /// <summary>Eski run'lari temizler (ornek son 30 gunden eski).</summary>
    Task PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken);
}
