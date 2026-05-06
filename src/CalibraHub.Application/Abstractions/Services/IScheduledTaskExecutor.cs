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

/// <summary>
/// Bir gorev calistirmasinin sonucu. ExecutedCommand opsiyonel — SQL prosedur turu
/// icin "EXEC [dbo].[sp_X] @p=N'value'" formatinda parametre tokenleri resolve edilmis
/// hali, HTTP turu icin "GET https://..." gibi anlamli bir komut ozeti tasiyabilir.
/// Run kaydina yazilir, history modal'da debug icin gosterilir.
/// </summary>
public sealed record TaskExecutionResult(int Status, string? Message, string? ExecutedCommand = null)
{
    public static TaskExecutionResult Success(string? message = null, string? executedCommand = null) => new(0, message, executedCommand);
    public static TaskExecutionResult Error(string message, string? executedCommand = null) => new(1, message, executedCommand);
}
