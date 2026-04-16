using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Worker;

public sealed class DocumentImportWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentImportWorker> _logger;

    public DocumentImportWorker(IServiceScopeFactory scopeFactory, ILogger<DocumentImportWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Belge ice aktarma worker'i baslatildi.");

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<IDocumentImportService>();
            var integratorSettingsRepository = scope.ServiceProvider.GetRequiredService<IIntegratorSettingsRepository>();

            try
            {
                var result = await importService.ImportFromActiveIntegratorsAsync(stoppingToken);
                var activeIntegrators = await integratorSettingsRepository.GetActiveAsync(stoppingToken);
                var nextDelay = GetNextPollingDelay(activeIntegrators);

                _logger.LogInformation(
                    "Import sonucu: {Imported} eklendi, {Skipped} atlandi. Sonraki calisma: {Delay} sn",
                    result.ImportedCount,
                    result.SkippedCount,
                    (int)nextDelay.TotalSeconds);

                await Task.Delay(nextDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Belge ice aktarma worker'inda hata olustu.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private static TimeSpan GetNextPollingDelay(IReadOnlyCollection<IntegratorSettings> settings)
    {
        if (settings.Count == 0)
        {
            return TimeSpan.FromMinutes(2);
        }

        var minPollingSeconds = settings.Min(x => x.PollingIntervalSeconds);
        return TimeSpan.FromSeconds(Math.Max(15, minPollingSeconds));
    }
}
