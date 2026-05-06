using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IOperationMachineTimeService
{
    Task<IReadOnlyCollection<OperationMachineTimeDto>> ListByOperationAsync(int operationId, CancellationToken ct);
    Task<int> SaveAsync(SaveOperationMachineTimeRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
