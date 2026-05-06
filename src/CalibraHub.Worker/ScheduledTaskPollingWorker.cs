using System.Diagnostics;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Services.Scheduling;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Worker;

/// <summary>
/// Scheduler orchestrator — scheduled_tasks tablosunu polling eder, NextRunAt gelen
/// BUILTIN disindaki gorevleri ilgili executor'a dispatch eder. BUILTIN gorevler kendi
/// BackgroundService'leri tarafindan zaten calistiriliyor; scheduler onlara dokunmaz.
///
/// Ayrica kullaniciya oturumu acilmadigi icin eklenmis yeni gorevlerin NextRunAt'i
/// null ise burada set edilir (initial scheduling).
/// </summary>
public sealed class ScheduledTaskPollingWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledTaskPollingWorker> _logger;

    public ScheduledTaskPollingWorker(IServiceScopeFactory scopeFactory, ILogger<ScheduledTaskPollingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduledTaskPollingWorker baslatildi (interval={Sec}s).", PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndDispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler polling hatasi.");
            }

            try { await Task.Delay(PollInterval, stoppingToken); } catch { break; }
        }
    }

    private async Task PollAndDispatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var taskRepo = scope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();

        var now = DateTime.UtcNow;

        // 1) NextRunAt=NULL olan (yeni eklenmis, hic calismamis) gorevleri initialize et
        var all = await taskRepo.GetAllAsync(ct);
        foreach (var t in all.Where(x => x.IsEnabled
                                      && x.TaskType != ScheduledTaskType.Builtin
                                      && x.ScheduleType != ScheduleType.Manual
                                      && x.NextRunAt is null))
        {
            var next = ScheduleEvaluator.ComputeNextRun(t, now);
            if (next.HasValue)
            {
                await taskRepo.ReportRunAsync(t.Id, t.LastRunStatus ?? -1, t.LastRunMessage, t.LastRunDurationMs, next, ct);
            }
        }

        // 2) Due olanlari fetch et + execute et
        var due = await taskRepo.GetDueTasksAsync(now, ct);
        if (due.Count == 0) return;

        foreach (var task in due)
        {
            if (ct.IsCancellationRequested) break;
            await TryDispatchAsync(scope, task, ct);
        }
    }

    private async Task TryDispatchAsync(IServiceScope scope, ScheduledTask task, CancellationToken ct)
    {
        var dispatcher = scope.ServiceProvider.GetRequiredService<IScheduledTaskDispatcher>();
        var taskRepo   = scope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();

        _logger.LogInformation("[{Name}#{Id}] dispatch (Schedule trigger) basladi.", task.Name, task.Id);
        var (ok, msg) = await dispatcher.TriggerNowAsync(task.Id, ct, RunTrigger.Schedule);
        _logger.LogInformation("[{Name}#{Id}] tamamlandi: ok={Ok}, msg={Msg}", task.Name, task.Id, ok, msg);

        // ONCE: tek seferlik gorev tamamlandi, disable et
        if (task.ScheduleType == ScheduleType.Once)
        {
            await taskRepo.SetEnabledAsync(task.Id, false, ct);
        }
    }
}
