using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Worker;

public sealed class ExchangeRateUpdateWorker : BackgroundService
{
    private const string TaskCode = "EXCHANGE_RATE";
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExchangeRateUpdateWorker> _logger;

    public ExchangeRateUpdateWorker(IServiceScopeFactory scopeFactory, ILogger<ExchangeRateUpdateWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Exchange rate update worker started.");

        // Startup: meta bilgiyi UPSERT et
        await TryRegisterAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = CalculateNextRun();
            var delay = nextRun - DateTime.Now;
            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation("Next exchange rate update at {NextRun}", nextRun);
                await Task.Delay(delay, stoppingToken);
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ICurrencyService>();
                var (ok, err, count) = await service.UpdateRatesFromTcmbAsync(stoppingToken);
                if (ok)
                {
                    _logger.LogInformation("Exchange rates updated: {Count} currencies", count);
                    await TryReportRunAsync(0, $"{count} kur guncellendi.", CalculateNextRun().ToUniversalTime(), stoppingToken);
                }
                else
                {
                    _logger.LogWarning("Exchange rate update failed: {Error}", err);
                    await TryReportRunAsync(1, err ?? "Bilinmeyen hata.", CalculateNextRun().ToUniversalTime(), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exchange rate update error.");
                await TryReportRunAsync(1, ex.Message, CalculateNextRun().ToUniversalTime(), stoppingToken);
            }

            // Hata durumunda 1 dakika sonra tekrar dene
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task TryRegisterAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
            await repo.UpsertRegistrationAsync(new ScheduledTask
            {
                Code                = TaskCode,
                Name                = "Doviz Kuru Guncelleme",
                Description         = "TCMB'den guncel doviz kurlarini cekip DB'ye yazar.",
                ScheduleDescription = "Her gun 09:00",
                IsEnabled           = true,
            }, ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "ScheduledTask register failed."); }
    }

    private async Task TryReportRunAsync(int status, string? msg, DateTime? nextRun, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
            await repo.ReportRunAsync(TaskCode, status, msg, null, nextRun, ct);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "ScheduledTask ReportRun failed."); }
    }


    private static DateTime CalculateNextRun()
    {
        var now = DateTime.Now;
        var today9am = now.Date.AddHours(9);
        return now < today9am ? today9am : today9am.AddDays(1);
    }
}
