using CalibraHub.Application.Abstractions.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Workflow;

/// <summary>
/// Her 15 dakikada bir aktif WorkflowInstanceNode'ları tarar.
/// TimeoutHours dolmuş task'ları SYSTEM_TIMEOUT aktörüyle reddeder.
/// </summary>
public sealed class WorkflowTimeoutEscalationJob(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowTimeoutEscalationJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[WorkflowTimeout] Job başlatıldı.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "[WorkflowTimeout] Çalışma hatası.");
            }
            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var instanceRepo      = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceRepository>();
        var engine            = scope.ServiceProvider.GetRequiredService<WorkflowEngine>();

        var timedOutNodes = await instanceRepo.GetTimedOutActiveNodesAsync(ct);
        var timedOut = 0;

        foreach (var node in timedOutNodes)
        {
            try
            {
                await engine.RejectStepAsync(
                    node.Id,
                    "Zaman aşımı — sistem tarafından otomatik reddedildi.",
                    "SYSTEM_TIMEOUT",
                    ct);
                timedOut++;
                logger.LogInformation("[WorkflowTimeout] InstanceNode {Id} zaman aşımıyla reddedildi.", node.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[WorkflowTimeout] InstanceNode {Id} reddedilemedi.", node.Id);
            }
        }

        logger.LogDebug("[WorkflowTimeout] Tarama tamamlandı: {Count} timeout.", timedOut);
    }
}
