using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IErpConnectionSettingsRepository
{
    Task<IReadOnlyCollection<ErpConnectionSettings>> GetAllAsync(CancellationToken cancellationToken);
    Task<ErpConnectionSettings?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(ErpConnectionSettings settings, CancellationToken cancellationToken);
    Task UpdateAsync(ErpConnectionSettings settings, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
