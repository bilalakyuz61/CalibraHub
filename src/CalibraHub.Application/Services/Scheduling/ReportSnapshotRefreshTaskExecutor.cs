using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services.Scheduling;

/// <summary>
/// Zamanlanmis gorev olarak bir rapor kaynaginin snapshot tablosunu yeniden olusturur
/// (dbo.ReportSnapshot_{sourceId}). Agir sorgu cron/interval ile tabloya yazilir; raporlar
/// oradan (hizli) okur.
///
/// ScheduledTask.ParametersJson: {"sourceId": N}
/// CompanyId task uzerinden gelir (snapshot ait oldugu sirket DB'sinde tutulur) — worker'da
/// HttpContext olmadigi icin MaterializeSourceForCompanyAsync sirket baglantisini acikca cozer.
/// </summary>
public sealed class ReportSnapshotRefreshTaskExecutor : IScheduledTaskExecutor
{
    private readonly IReportQueryService _query;

    public ReportSnapshotRefreshTaskExecutor(IReportQueryService query)
    {
        _query = query;
    }

    public ScheduledTaskType SupportedType => ScheduledTaskType.ReportSnapshotRefresh;

    public async Task<TaskExecutionResult> ExecuteAsync(ScheduledTask task, CancellationToken cancellationToken)
    {
        if (task.CompanyId is null or 0)
            return TaskExecutionResult.Error("CompanyId zorunlu — snapshot sirket DB'sinde tutulur.");
        if (string.IsNullOrWhiteSpace(task.ParametersJson))
            return TaskExecutionResult.Error("ParametersJson zorunlu — {\"sourceId\": N} formatinda.");

        int sourceId;
        try
        {
            using var doc = JsonDocument.Parse(task.ParametersJson);
            if (!doc.RootElement.TryGetProperty("sourceId", out var el) || el.ValueKind != JsonValueKind.Number)
                return TaskExecutionResult.Error("ParametersJson'da sourceId (number) bekleniyor.");
            sourceId = el.GetInt32();
        }
        catch (Exception ex)
        {
            return TaskExecutionResult.Error("ParametersJson parse hatasi: " + ex.Message);
        }

        if (sourceId <= 0)
            return TaskExecutionResult.Error("Gecersiz sourceId.");

        try
        {
            var rows = await _query.MaterializeSourceForCompanyAsync(task.CompanyId.Value, sourceId, cancellationToken);
            return TaskExecutionResult.Success(
                $"Snapshot #{sourceId} yenilendi ({rows} satir).",
                executedCommand: $"MaterializeSourceForCompany(company={task.CompanyId}, source={sourceId})");
        }
        catch (Exception ex)
        {
            return TaskExecutionResult.Error(
                $"Snapshot #{sourceId} yenileme hatasi: {ex.Message}",
                executedCommand: $"MaterializeSourceForCompany(company={task.CompanyId}, source={sourceId})");
        }
    }
}
