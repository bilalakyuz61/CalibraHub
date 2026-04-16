using CalibraHub.Application.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Worker;

public sealed class ExchangeRateUpdateWorker : BackgroundService
{
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
                    _logger.LogInformation("Exchange rates updated: {Count} currencies", count);
                else
                    _logger.LogWarning("Exchange rate update failed: {Error}", err);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exchange rate update error.");
            }

            // Hata durumunda 1 saat sonra tekrar dene
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private static DateTime CalculateNextRun()
    {
        var now = DateTime.Now;
        var today9am = now.Date.AddHours(9);
        return now < today9am ? today9am : today9am.AddDays(1);
    }
}
