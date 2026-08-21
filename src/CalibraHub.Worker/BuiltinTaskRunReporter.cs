using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Worker;

/// <summary>
/// BUILTIN zamanlanmis gorevlerin calisma raporunu tek noktadan yazar.
///
/// Neden var (2026-08-22): Her worker kendi icinde GetByNameAsync + ReportRunAsync
/// kopyasini tasiyordu ve hicbiri <see cref="IScheduledTaskRunRepository"/> uzerine
/// satir YAZMIYORDU. ReportRunAsync yalnizca ScheduledTask.LastRun* alanlarini gunceller;
/// "Calisma Gecmisi" modali ise ScheduledTaskRun tablosunu okur. Sonuc: sistem gorevleri
/// icin gecmis ekrani her zaman bos aciliyordu (kullanici bunu "ekran acilmiyor" olarak
/// bildirdi). Dispatcher'dan gecen (non-BUILTIN) gorevlerde run satirini dispatcher yazar;
/// BUILTIN gorevler dispatcher'a hic ugramadigi icin burasi tek yazim noktasidir.
///
/// DIKKAT: <see cref="IScheduledTaskRepository.ReportRunAsync"/> icine tasinamaz —
/// ScheduledTaskPollingWorker onu yalnizca NextRunAt backfill'i icin de cagiriyor
/// (calisma degil), dispatcher ise zaten kendi run satirini yaziyor; merkezilestirilirse
/// ikisi de sahte/ciftlenmis gecmis satiri uretirdi.
/// </summary>
internal static class BuiltinTaskRunReporter
{
    /// <summary>
    /// Gorev adindan Id cozer, LastRun* alanlarini gunceller ve gecmise bir calisma satiri yazar.
    /// Gorev kaydi bulunamazsa sessizce atlar (bir sonraki turda registration tamamlanmis olur).
    /// Hata firlatmaz — raporlama, isin kendisini asla bozmamalidir; ama yutmaz da: loglar.
    /// </summary>
    public static async Task ReportAsync(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        string taskName,
        int status,
        string? message,
        int? durationMs,
        DateTime? nextRunUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var taskRepo = scope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
            var task = await taskRepo.GetByNameAsync(taskName, cancellationToken);
            if (task is null)
            {
                logger.LogWarning("Zamanlanmis gorev kaydi bulunamadi, calisma raporu atlandi: {Task}", taskName);
                return;
            }

            await taskRepo.ReportRunAsync(task.Id, status, message, durationMs, nextRunUtc, cancellationToken);

            var completedAt = DateTime.UtcNow;
            var runRepo = scope.ServiceProvider.GetRequiredService<IScheduledTaskRunRepository>();
            await runRepo.CreateAsync(new ScheduledTaskRun
            {
                TaskId      = task.Id,
                // TaskCode denormalize snapshot — dispatcher da task adini yaziyor.
                TaskCode    = task.Name,
                StartedAt   = completedAt.AddMilliseconds(-(durationMs ?? 0)),
                CompletedAt = completedAt,
                Status      = status,
                Message     = message,
                DurationMs  = durationMs,
                Trigger     = RunTrigger.Schedule,
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Zamanlanmis gorev calisma raporu yazilamadi: {Task}", taskName);
        }
    }
}
