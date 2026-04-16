using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IIntegrationApiProfileRepository
{
    Task<IReadOnlyCollection<IntegrationApiProfile>> GetByCompanyAsync(int companyId, CancellationToken ct);
    Task<IntegrationApiProfile?> GetByIdAsync(Guid id, CancellationToken ct);
    Task UpsertAsync(IntegrationApiProfile profile, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
