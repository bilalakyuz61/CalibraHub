using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services.Scheduling;

/// <summary>
/// Hem Web manual "Simdi Calistir" hem de Worker'in scheduler dispatch'i icin kullanilir.
/// Oncul gorev (PrerequisiteTaskId) varsa once o calistirilir; oncul basarili olursa
/// ana gorev calisir. Cycle detection runtime'da visit-set ile.
/// </summary>
public interface IScheduledTaskDispatcher
{
    /// <summary>
    /// Verilen gorevi hemen calistir. Trigger varsayilan MANUAL — scheduler/polling
    /// tetikleyecekse RunTrigger.Schedule gecmeli.
    /// </summary>
    Task<(bool ok, string? message)> TriggerNowAsync(
        int taskId,
        CancellationToken cancellationToken,
        RunTrigger trigger = RunTrigger.Manual);
}
