using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IRoutingRepository
{
    Task<IReadOnlyCollection<RoutingDto>> ListAsync(int? itemId, CancellationToken ct);
    Task<RoutingDto?> GetAsync(int id, CancellationToken ct);
    Task<IReadOnlyCollection<RoutingOperationDto>> GetOperationsAsync(int routingId, CancellationToken ct);
    Task<int> SaveAsync(Routing header, IReadOnlyList<RoutingOperation>? operations, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    /// <summary>Bir operasyonun kullanıldığı rotaları döner — operasyon detay tab'ında listelenir.</summary>
    Task<IReadOnlyCollection<RoutingDto>> ListByOperationAsync(int operationId, CancellationToken ct);
}
