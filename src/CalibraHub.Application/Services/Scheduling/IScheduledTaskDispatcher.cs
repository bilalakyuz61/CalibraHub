namespace CalibraHub.Application.Services.Scheduling;

/// <summary>
/// Web tarafindan manual "Run Now" tetiklemesi icin kullanilir.
/// Gorev DB'den okunur, uygun executor bulunur, calistirilir ve run history yazilir.
/// Web process Worker process'ten ayri calistigi icin her iki tarafta da implementation
/// calisir — Web process'ten tetikle-et-an-and-return, gorev Worker'in polling'i ile
/// normal akista tekrar calisabilir.
/// </summary>
public interface IScheduledTaskDispatcher
{
    /// <summary>Verilen gorevi hemen calistir. Trigger MANUAL olarak kaydedilir.</summary>
    Task<(bool ok, string? message)> TriggerNowAsync(string code, CancellationToken cancellationToken);
}
