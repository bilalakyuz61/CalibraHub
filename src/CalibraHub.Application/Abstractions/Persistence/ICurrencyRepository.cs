using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ICurrencyRepository
{
    Task<IReadOnlyCollection<Currency>> GetAllAsync(CancellationToken ct);
    Task<Currency?> GetByIdAsync(int id, CancellationToken ct);
    Task<int> AddAsync(Currency entity, CancellationToken ct);
    Task UpdateAsync(Currency entity, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
