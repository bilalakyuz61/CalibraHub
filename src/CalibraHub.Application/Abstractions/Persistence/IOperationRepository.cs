using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IOperationRepository
{
    Task<IReadOnlyCollection<Operation>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<Operation?> GetAsync(int id, CancellationToken ct);
    Task<int> UpsertAsync(Operation entity, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
