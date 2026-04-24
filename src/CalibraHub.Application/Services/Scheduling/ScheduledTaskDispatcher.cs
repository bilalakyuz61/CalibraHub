using System.Diagnostics;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Scheduling;

/// <summary>
/// Manual/programmatic trigger dispatcher — BUILTIN gorevler icin acquire lock,
/// diger gorev tipleri icin ilgili executor'u cagirir. Her iki yolda da run history
/// yazilir ve NextRunAt scheduler tarafindan yeniden hesaplanir.
/// </summary>
public sealed class ScheduledTaskDispatcher : IScheduledTaskDispatcher
{
    private readonly IScheduledTaskRepository _taskRepo;
    private readonly IScheduledTaskRunRepository _runRepo;
    private readonly IEnumerable<IScheduledTaskExecutor> _executors;
    private readonly ILogger<ScheduledTaskDispatcher> _logger;

    public ScheduledTaskDispatcher(
        IScheduledTaskRepository taskRepo,
        IScheduledTaskRunRepository runRepo,
        IEnumerable<IScheduledTaskExecutor> executors,
        ILogger<ScheduledTaskDispatcher> logger)
    {
        _taskRepo  = taskRepo;
        _runRepo   = runRepo;
        _executors = executors;
        _logger    = logger;
    }

    public async Task<(bool ok, string? message)> TriggerNowAsync(string code, CancellationToken cancellationToken)
    {
        var task = await _taskRepo.GetByCodeAsync(code, cancellationToken);
        if (task is null) return (false, $"Gorev bulunamadi: {code}");
        if (!task.IsEnabled) return (false, "Gorev devre disi — once aktiflestirin.");

        // BUILTIN gorevler icin executor yok; dispatcher bu tipi calistirmaz.
        if (task.TaskType == ScheduledTaskType.Builtin)
        {
            return (false, "BUILTIN gorevler scheduler orchestrator tarafindan degil, kendi BackgroundService'leri tarafindan calistirilir. Manuel tetikleme desteklenmiyor.");
        }

        var executor = _executors.FirstOrDefault(e => e.SupportedType == task.TaskType);
        if (executor is null)
        {
            return (false, $"TaskType={task.TaskType} icin executor bulunamadi.");
        }

        if (!await _taskRepo.TryAcquireLockAsync(code, cancellationToken))
        {
            return (false, "Gorev zaten calisiyor.");
        }

        var runId = await _runRepo.CreateAsync(new ScheduledTaskRun
        {
            TaskId    = task.Id,
            TaskCode  = task.Code,
            StartedAt = DateTime.UtcNow,
            Status    = 2, // running
            Trigger   = RunTrigger.Manual,
        }, cancellationToken);

        var sw = Stopwatch.StartNew();
        TaskExecutionResult result;
        try
        {
            result = await executor.ExecuteAsync(task, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dispatcher error for task {Code}.", code);
            result = TaskExecutionResult.Error(ex.Message);
        }
        finally
        {
            sw.Stop();
            var durationMs = (int)sw.ElapsedMilliseconds;
            await _runRepo.CompleteAsync(runId, 0, null, durationMs, cancellationToken); // dummy, gercek status alttaki call'da
            await _taskRepo.ReleaseLockAsync(code, cancellationToken);
        }

        var nextRun = ScheduleEvaluator.ComputeNextRun(task, DateTime.UtcNow);
        await _runRepo.CompleteAsync(runId, result.Status, result.Message, (int)sw.ElapsedMilliseconds, cancellationToken);
        await _taskRepo.ReportRunAsync(code, result.Status, result.Message, (int)sw.ElapsedMilliseconds, nextRun, cancellationToken);

        return (result.Status == 0, result.Message);
    }
}
