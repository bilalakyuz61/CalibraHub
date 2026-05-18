using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services.Scheduling;

/// <summary>
/// Zamanlanmis gorev olarak bir entegrasyonu calistirir.
///
/// ScheduledTask.ParametersJson formati:
///   { "integrationId": 12, "recordId": "ABC-123" }
///
/// recordId opsiyonel — verilmezse IntegrationRunner null gecer (wizard test akisinda
/// oldugu gibi, sample fallback data ile calisir). V2: recordId yoksa "tum new kayitlar"
/// gibi batch semantik destekleri (DueRecordsResolver).
///
/// Bu executor "TaskType=Integration" tasklari ScheduledTaskDispatcher tarafindan
/// otomatik tetiklendiginde devreye girer. UI'da Wizard Step 5'te trigger=Cron
/// secildiginde otomatik bir ScheduledTask kaydi yaratmak yerine V1'de kullanici
/// kendi /Admin/ScheduledTasks ekranindan task tanimlar (TaskType=Integration secip
/// integrationId verir).
/// </summary>
public sealed class IntegrationTaskExecutor : IScheduledTaskExecutor
{
    private readonly IIntegrationRunner _runner;

    public IntegrationTaskExecutor(IIntegrationRunner runner)
    {
        _runner = runner;
    }

    public ScheduledTaskType SupportedType => ScheduledTaskType.Integration;

    public async Task<TaskExecutionResult> ExecuteAsync(ScheduledTask task, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task.ParametersJson))
            return TaskExecutionResult.Error("ParametersJson zorunlu — {\"integrationId\": N} formatinda.");

        int integrationId;
        string? recordId;
        try
        {
            using var doc = JsonDocument.Parse(task.ParametersJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("integrationId", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
                return TaskExecutionResult.Error("ParametersJson'da integrationId (number) bekleniyor.");
            integrationId = idEl.GetInt32();
            recordId = root.TryGetProperty("recordId", out var rEl) && rEl.ValueKind == JsonValueKind.String
                ? rEl.GetString()
                : null;
        }
        catch (Exception ex)
        {
            return TaskExecutionResult.Error("ParametersJson parse hatasi: " + ex.Message);
        }

        if (integrationId <= 0)
            return TaskExecutionResult.Error("Gecersiz integrationId.");

        var triggeredBy = string.IsNullOrWhiteSpace(task.Name) ? "scheduled" : "scheduled:" + task.Name;
        var result = await _runner.RunAsync(
            integrationId,
            string.IsNullOrWhiteSpace(recordId) ? null : recordId,
            IntegrationTriggerType.Cron,
            triggeredBy,
            cancellationToken);

        if (result.Success)
        {
            return TaskExecutionResult.Success(
                $"Integration #{integrationId} calistirildi (HTTP {result.HttpStatusCode}, RunId={result.RunId}).",
                executedCommand: $"IntegrationRunner.RunAsync({integrationId}, recordId={recordId ?? "null"})");
        }
        return TaskExecutionResult.Error(
            $"Integration #{integrationId} basarisiz: {result.ErrorMessage ?? "(detay yok)"}",
            executedCommand: $"IntegrationRunner.RunAsync({integrationId}, recordId={recordId ?? "null"})");
    }
}
