using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services.Scheduling;

/// <summary>
/// Zamanlanmis gorev olarak TCMB'den doviz kurlarini cekip DB'ye yazar.
/// HttpApiTaskExecutor yalnizca response'u goz ardi eder; burasi ise
/// CurrencyService'teki hafta sonu/tatil fallback mantigini oldugu gibi kullanir.
/// </summary>
public sealed class CurrencyRefreshTaskExecutor : IScheduledTaskExecutor
{
    private readonly ICurrencyService _currencyService;

    public CurrencyRefreshTaskExecutor(ICurrencyService currencyService)
    {
        _currencyService = currencyService;
    }

    public ScheduledTaskType SupportedType => ScheduledTaskType.CurrencyRefresh;

    public async Task<TaskExecutionResult> ExecuteAsync(ScheduledTask task, CancellationToken cancellationToken)
    {
        var (ok, error, count) = await _currencyService.UpdateRatesFromTcmbAsync(cancellationToken);
        return ok
            ? TaskExecutionResult.Success($"{count} kur guncellendi.")
            : TaskExecutionResult.Error(error ?? "TCMB'den kur cekilemedi.");
    }
}
