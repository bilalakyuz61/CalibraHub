using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IWorkOrderOperationService
{
    Task<IReadOnlyCollection<WorkOrderOperationDto>> GetByWorkOrderAsync(int workOrderId, CancellationToken ct);
    Task<IReadOnlyCollection<WorkOrderOperationDto>> GetQueueByMachineAsync(int machineId, CancellationToken ct);
    Task<WorkOrderOperationDto?> GetAsync(int id, CancellationToken ct);
    Task<int> SaveAsync(SaveWorkOrderOperationRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task ExplodeFromRoutingAsync(int workOrderId, int routingId, CancellationToken ct);
    Task StartAsync(StartOperationRequest request, CancellationToken ct);
    Task PartialCompleteAsync(PartialCompleteOperationRequest request, CancellationToken ct);
    Task CompleteAsync(CompleteOperationRequest request, CancellationToken ct);
}
