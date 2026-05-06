using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IRoutingService
{
    Task<IReadOnlyCollection<RoutingDto>> ListAsync(int? itemId, CancellationToken ct);
    Task<RoutingDto?> GetAsync(int id, CancellationToken ct);
    Task<IReadOnlyCollection<RoutingOperationDto>> GetOperationsAsync(int routingId, CancellationToken ct);
    Task<int> SaveAsync(SaveRoutingRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task<IReadOnlyCollection<RoutingDto>> ListByOperationAsync(int operationId, CancellationToken ct);
}
