using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IIntegratorSettingsRepository
{
    Task<IReadOnlyCollection<IntegratorSettings>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<IntegratorSettings>> GetActiveAsync(CancellationToken cancellationToken);
    Task<IntegratorSettings?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<IntegratorSettings?> GetByCompanyIdAsync(int companyId, CancellationToken cancellationToken);
    Task<int> AddAsync(IntegratorSettings settings, CancellationToken cancellationToken);
    Task UpdateAsync(IntegratorSettings settings, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
