using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Bir zamanlanmis gorev turunu calistiran executor.
/// Her ScheduledTaskType'in kendi implementation'i olur — DI ile resolve edilir.
/// Scheduler, task.TaskType'a gore ilgili executor'i secer.
/// </summary>
public interface IScheduledTaskExecutor
{
    /// <summary>Hangi TaskType icin gecerli oldugunu belirtir.</summary>
    ScheduledTaskType SupportedType { get; }

    /// <summary>Gorevi calistir ve sonucunu doner. Exception firlatmamali — TaskExecutionResult.Status=1 + Message ile hata bildir.</summary>
    Task<TaskExecutionResult> ExecuteAsync(ScheduledTask task, CancellationToken cancellationToken);
}

/// <summary>Bir gorev calistirmasinin sonucu.</summary>
public sealed record TaskExecutionResult(int Status, string? Message)
{
    public static TaskExecutionResult Success(string? message = null) => new(0, message);
    public static TaskExecutionResult Error(string message) => new(1, message);
}
