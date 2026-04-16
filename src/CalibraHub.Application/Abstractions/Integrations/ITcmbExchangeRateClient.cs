using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Integrations;

public interface ITcmbExchangeRateClient
{
    Task<IReadOnlyCollection<ExchangeRate>> GetDailyRatesAsync(CancellationToken ct);
    Task<IReadOnlyCollection<ExchangeRate>> GetRatesForDateAsync(DateTime date, CancellationToken ct);
}
