using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IExchangeRateRepository
{
    Task<IReadOnlyCollection<ExchangeRate>> GetLatestRatesAsync(CancellationToken ct);
    Task<IReadOnlyCollection<ExchangeRate>> GetRatesForDateAsync(DateTime date, CancellationToken ct);
    Task<ExchangeRate?> GetRateAsync(string currencyCode, DateTime date, CancellationToken ct);
    Task SaveRatesAsync(IReadOnlyCollection<ExchangeRate> rates, CancellationToken ct);
    Task DeleteRateAsync(string currencyCode, DateTime date, CancellationToken ct);
    Task<IReadOnlyCollection<ExchangeRate>> GetRatesInRangeAsync(string currencyCode, DateTime from, DateTime to, CancellationToken ct);
    Task<IReadOnlyCollection<ExchangeRate>> GetAllRatesInRangeAsync(DateTime from, DateTime to, CancellationToken ct);
}
