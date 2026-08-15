using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services.Scheduling;

/// <summary>
/// Zamanlanmis gorev olarak bir veritabani ice aktarim isini calistirir.
///
/// ScheduledTask.ParametersJson formati:
///   { "jobId": 12 }
///
/// Calistirma yolu elle tetiklemeyle AYNIDIR (IDataImportService.RunAsync): on prosedur →
/// kaynaktan salt-okunur okuma → handler ile yazma → son prosedur → DataImportRun kaydi.
/// Anahtar alan zorunlulugu is tanimi dogrulamasinda uygulanir, bu yol onu atlayamaz.
/// </summary>
public sealed class DataImportTaskExecutor : IScheduledTaskExecutor
{
    private readonly IDataImportService _dataImport;

    public DataImportTaskExecutor(IDataImportService dataImport) => _dataImport = dataImport;

    public ScheduledTaskType SupportedType => ScheduledTaskType.DataImport;

    public async Task<TaskExecutionResult> ExecuteAsync(ScheduledTask task, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task.ParametersJson))
            return TaskExecutionResult.Error("ParametersJson zorunlu — {\"jobId\": N} formatinda.");

        int jobId;
        try
        {
            using var doc = JsonDocument.Parse(task.ParametersJson);
            if (!doc.RootElement.TryGetProperty("jobId", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
                return TaskExecutionResult.Error("ParametersJson'da jobId (number) bekleniyor.");
            jobId = idEl.GetInt32();
        }
        catch (Exception ex)
        {
            return TaskExecutionResult.Error("ParametersJson parse hatasi: " + ex.Message);
        }

        if (jobId <= 0) return TaskExecutionResult.Error("Gecersiz jobId.");

        var triggeredBy = string.IsNullOrWhiteSpace(task.Name) ? "scheduled" : "scheduled:" + task.Name;
        var command = $"DataImportService.RunAsync(jobId={jobId})";

        var result = await _dataImport.RunAsync(
            jobId, DataImportTriggerType.Cron, triggeredBy, userId: null, cancellationToken);

        var run = result.Run;
        if (result.Ok && run is not null)
        {
            var warn = run.Status == DataImportRunStatus.SucceededWithWarnings ? " (uyarili)" : string.Empty;
            return TaskExecutionResult.Success(
                $"Ice aktarim #{jobId} calistirildi{warn} — okunan {run.RowsRead}, eklenen {run.RowsInserted}, " +
                $"guncellenen {run.RowsUpdated}, hatali {run.RowsFailed} (RunId={run.Id}).",
                executedCommand: command);
        }

        return TaskExecutionResult.Error(
            $"Ice aktarim #{jobId} basarisiz: {result.Error ?? run?.ErrorMessage ?? "(detay yok)"}",
            executedCommand: command);
    }
}
