using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IOperationMachineTimeRepository
{
    /// <summary>
    /// Operasyona göre makine süre kayıtları. <paramref name="routingId"/> null ise yalnız rota-bağımsız
    /// (RoutingId IS NULL) ortak satırlar; dolu ise ortak satırlar + o rotaya özel satırlar döner.
    /// </summary>
    Task<IReadOnlyCollection<OperationMachineTimeDto>> ListByOperationAsync(int operationId, int? routingId, CancellationToken ct);
    Task<int> SaveAsync(OperationMachineTime entity, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>Bir CardGroup kaydının CardType değeri (bulunamazsa null). Grup tipi doğrulaması için.</summary>
    Task<int?> GetCardGroupCardTypeAsync(int cardGroupId, CancellationToken ct);

    /// <summary>Birimin ürünün birim setinde (Items.UnitId baz VEYA ItemUnits satırları) olup olmadığı.</summary>
    Task<bool> IsUnitInItemSetAsync(int itemId, int unitId, CancellationToken ct);
}
