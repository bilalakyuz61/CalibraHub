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
                await taskRepo.ReportRunAsync(t.Code, t.LastRunStatus ?? -1, t.LastRunMessage, t.LastRunDurationMs, next, ct);
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
        var taskRepo = scope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
        var runRepo  = scope.ServiceProvider.GetRequiredService<IScheduledTaskRunRepository>();
        var executors = scope.ServiceProvider.GetServices<IScheduledTaskExecutor>();
        var executor = executors.FirstOrDefault(e => e.SupportedType == task.TaskType);

        if (executor is null)
        {
            _logger.LogWarning("[{Code}] TaskType={Type} icin executor yok — gorev NextRun 1 saat ilerletiliyor.", task.Code, task.TaskType);
            await taskRepo.ReportRunAsync(task.Code, 1, $"Executor bulunamadi: {task.TaskType}", 0, DateTime.UtcNow.AddHours(1), ct);
            return;
        }

        if (!await taskRepo.TryAcquireLockAsync(task.Code, ct))
        {
            _logger.LogDebug("[{Code}] baska instance calistiriyor, atlandi.", task.Code);
            return;
        }

        var runId = await runRepo.CreateAsync(new ScheduledTaskRun
        {
            TaskId    = task.Id,
            TaskCode  = task.Code,
            StartedAt = DateTime.UtcNow,
            Status    = 2,
            Trigger   = RunTrigger.Schedule,
        }, ct);

        var sw = Stopwatch.StartNew();
        TaskExecutionResult result;
        try
        {
            _logger.LogInformation("[{Code}] executor={Executor} dispatch basladi.", task.Code, executor.GetType().Name);
            result = await executor.ExecuteAsync(task, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Code}] executor exception.", task.Code);
            result = TaskExecutionResult.Error(ex.Message);
        }
        sw.Stop();
        var durationMs = (int)sw.ElapsedMilliseconds;

        var nextRun = ScheduleEvaluator.ComputeNextRun(task, DateTime.UtcNow);
        if (task.ScheduleType == ScheduleType.Once)
        {
            // ONCE calisti → disable et
            await taskRepo.SetEnabledAsync(task.Code, false, ct);
        }

        await runRepo.CompleteAsync(runId, result.Status, result.Message, durationMs, ct);
        await taskRepo.ReportRunAsync(task.Code, result.Status, result.Message, durationMs, nextRun, ct);
        await taskRepo.ReleaseLockAsync(task.Code, ct);

        _logger.LogInformation("[{Code}] tamamlandi: status={Status}, ms={Ms}, next={Next}",
            task.Code, result.Status, durationMs, nextRun);
    }
}
