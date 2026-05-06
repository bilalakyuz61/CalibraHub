using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IOperationService
{
    Task<IReadOnlyCollection<OperationDto>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<OperationDto?> GetAsync(int id, CancellationToken ct);
    Task<int> SaveAsync(SaveOperationRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
