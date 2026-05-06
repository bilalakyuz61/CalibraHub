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
///
/// Oncul gorev (PrerequisiteTaskId) destegi: bir gorev tetiklendiginde onculu varsa
/// once oncul calistirilir; oncul basarisiz olursa ana gorev iptal edilir. Cycle
/// detection runtime'da visit-set ile yapilir (A→B→A patlamadan yakalanir).
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

    public Task<(bool ok, string? message)> TriggerNowAsync(
        int taskId,
        CancellationToken cancellationToken,
        RunTrigger trigger = RunTrigger.Manual)
        => TriggerInternalAsync(taskId, new HashSet<int>(), trigger, cancellationToken);

    private async Task<(bool ok, string? message)> TriggerInternalAsync(
        int taskId, HashSet<int> visited, RunTrigger trigger, CancellationToken cancellationToken)
    {
        var task = await _taskRepo.GetByIdAsync(taskId, cancellationToken);
        if (task is null) return (false, $"Gorev bulunamadi: id={taskId}");
        if (!task.IsEnabled) return (false, $"Gorev devre disi: {task.Name}");

        // Cycle detection — A→B→A gibi sonsuz dongulere karsi
        if (!visited.Add(task.Id))
            return (false, $"Oncul dongusu yakalandi (gorev: {task.Name}). Tanimi duzeltin.");

        // Oncul gorev varsa once onu calistir
        if (task.PrerequisiteTaskId.HasValue && task.PrerequisiteTaskId.Value > 0)
        {
            var prereq = await _taskRepo.GetByIdAsync(task.PrerequisiteTaskId.Value, cancellationToken);
            if (prereq is null)
                return (false, $"Oncul gorev bulunamadi (id={task.PrerequisiteTaskId}). Tanimi duzeltin.");

            var (preOk, preMsg) = await TriggerInternalAsync(prereq.Id, visited, trigger, cancellationToken);
            if (!preOk)
                return (false, $"Oncul gorev '{prereq.Name}' basarisiz: {preMsg ?? "(detay yok)"} — ana gorev iptal.");
        }

        // BUILTIN gorevler dispatcher tarafindan calistirilmaz
        if (task.TaskType == ScheduledTaskType.Builtin)
            return (false, "BUILTIN gorevler scheduler orchestrator tarafindan degil, kendi BackgroundService'leri tarafindan calistirilir. Manuel tetikleme desteklenmiyor.");

        var executor = _executors.FirstOrDefault(e => e.SupportedType == task.TaskType);
        if (executor is null)
            return (false, $"TaskType={task.TaskType} icin executor bulunamadi.");

        if (!await _taskRepo.TryAcquireLockAsync(task.Id, cancellationToken))
            return (false, "Gorev zaten calisiyor.");

        var runId = await _runRepo.CreateAsync(new ScheduledTaskRun
        {
            TaskId    = task.Id,
            // TaskCode kolonu (DB) korundu — denormalize snapshot olarak task adini yaziyoruz.
            TaskCode  = task.Name,
            StartedAt = DateTime.UtcNow,
            Status    = 2,
            Trigger   = trigger,
        }, cancellationToken);

        var sw = Stopwatch.StartNew();
        TaskExecutionResult result;
        try
        {
            result = await executor.ExecuteAsync(task, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dispatcher error for task id={Id} ({Name}).", task.Id, task.Name);
            result = TaskExecutionResult.Error(ex.Message);
        }
        finally
        {
            sw.Stop();
            await _taskRepo.ReleaseLockAsync(task.Id, cancellationToken);
        }

        var nextRun = ScheduleEvaluator.ComputeNextRun(task, DateTime.UtcNow);
        await _runRepo.CompleteAsync(runId, result.Status, result.Message, (int)sw.ElapsedMilliseconds, result.ExecutedCommand, cancellationToken);
        await _taskRepo.ReportRunAsync(task.Id, result.Status, result.Message, (int)sw.ElapsedMilliseconds, nextRun, cancellationToken);

        return (result.Status == 0, result.Message);
    }
}
