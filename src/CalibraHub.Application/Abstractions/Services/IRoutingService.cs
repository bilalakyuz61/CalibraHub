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

    Task<IReadOnlyList<RoutingWithOpsDto>> GetAllWithOperationsAsync(CancellationToken ct);

    Task<IReadOnlyCollection<RoutingItemMapDto>> GetItemMapsAsync(int routingId, CancellationToken ct);
    Task<int> AddItemMapAsync(int routingId, int itemId, int? configId, CancellationToken ct);
    Task DeleteItemMapAsync(int id, CancellationToken ct);
}
