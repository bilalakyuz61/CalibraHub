using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface ICurrencyService
{
    Task<IReadOnlyCollection<CurrencyDto>> GetAllAsync(CancellationToken ct);
    Task<IReadOnlyCollection<CurrencyDto>> GetAllForDateAsync(DateTime date, CancellationToken ct);
    Task<CurrencyDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<(bool Success, string? Error, int? Id)> CreateAsync(CreateCurrencyRequest request, CancellationToken ct);
    Task<(bool Success, string? Error)> UpdateAsync(UpdateCurrencyRequest request, CancellationToken ct);
    Task<(bool Success, string? Error)> DeleteAsync(int id, CancellationToken ct);
    Task<(bool Success, string? Error, int Count)> UpdateRatesFromTcmbAsync(CancellationToken ct);
    Task<(bool Success, string? Error, int Count)> UpdateRatesFromTcmbAsync(DateTime date, CancellationToken ct);
    Task<(bool Success, string? Error, int TotalCount)> UpdateRatesFromTcmbBulkAsync(DateTime from, DateTime to, CancellationToken ct);
}
