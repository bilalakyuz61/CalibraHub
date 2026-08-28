using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Domain.Enums;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Worker;

public sealed class DocumentImportWorker : BackgroundService
{
    private const string TaskName = "Belge Ice Aktarim";
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

        // Startup registration
        try
        {
            using var regScope = _scopeFactory.CreateScope();
            var repo = regScope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
            await repo.UpsertRegistrationAsync(new ScheduledTask
            {
                Name                = TaskName,
                Description         = "Aktif entegratorlerden belgeleri cekip DB'ye aktarir.",
                ScheduleDescription = "Entegrator polling interval'ine gore",
                IsEnabled           = true,
            }, stoppingToken);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "ScheduledTask register failed."); }

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<IDocumentImportService>();
            var integratorSettingsRepository = scope.ServiceProvider.GetRequiredService<IIntegratorSettingsRepository>();

            try
            {
                var result = await importService.ImportFromActiveIntegratorsAsync(stoppingToken);

                // CEVRIMDISI (ERP) yol AYNI zamanlayiciya bagli — ikinci bir worker/dongu
                // kurulmadi. Sirket parametresi Offline degilse bu cagri hicbir sey yapmaz
                // ve bos sonuc doner, yani her turda guvenle cagrilir.
                var offline = await importService.ImportFromOfflineSourceAsync(stoppingToken);

                var activeIntegrators = await integratorSettingsRepository.GetActiveAsync(stoppingToken);
                var nextDelay = GetNextPollingDelay(activeIntegrators);

                // CEVRIMDISI yol aktifse tarama araligi KENDI parametresinden gelir.
                // Aksi halde Parametreler ekranindaki "Tarama Araligi" alani hicbir sey
                // yapmazdi (sessiz no-op) — deger entegrator ayarlarindan hesaplaniyordu,
                // ama cevrimdisi kurulumda hic entegrator YOKTUR.
                var parameters = scope.ServiceProvider.GetRequiredService<ICompanyParameterService>();
                var methodRaw = await parameters.GetStringAsync(
                    EDocumentParameters.FormCode, EDocumentParameters.IngestMethodKey, stoppingToken);

                if (string.Equals(methodRaw, nameof(EDocumentIngestSource.Offline), StringComparison.OrdinalIgnoreCase))
                {
                    var offlineSeconds = await parameters.GetIntAsync(
                        EDocumentParameters.FormCode, EDocumentParameters.PollIntervalSecondsKey, stoppingToken)
                        ?? EDocumentParameters.DefaultPollIntervalSeconds;
                    nextDelay = TimeSpan.FromSeconds(Math.Clamp(offlineSeconds, 30, 86400));
                }

                var imported = result.ImportedCount + offline.ImportedCount;
                var skipped  = result.SkippedCount + offline.SkippedCount;

                // Cevrimdisi tarafin notlari (baglanti secilmemis, kaynak okunamadi vb.)
                // SESSIZ KALMAZ — yoksa "hic belge gelmiyor" sikayeti teshis edilemezdi.
                foreach (var note in offline.Notes)
                    _logger.LogWarning("[E-Belge/Offline] {Note}", note);

                _logger.LogInformation(
                    "Import sonucu: {Imported} eklendi, {Skipped} atlandi (cevrimdisi: {OffIn}/{OffSkip}). Sonraki calisma: {Delay} sn",
                    imported,
                    skipped,
                    offline.ImportedCount,
                    offline.SkippedCount,
                    (int)nextDelay.TotalSeconds);

                await BuiltinTaskRunReporter.ReportAsync(_scopeFactory, _logger, TaskName, 0,
                    $"{imported} eklendi, {skipped} atlandi.",
                    null, DateTime.UtcNow.Add(nextDelay), stoppingToken);

                await Task.Delay(nextDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Belge ice aktarma worker'inda hata olustu.");
                await BuiltinTaskRunReporter.ReportAsync(_scopeFactory, _logger, TaskName, 1,
                    ex.Message, null, DateTime.UtcNow.AddSeconds(30), stoppingToken);
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
