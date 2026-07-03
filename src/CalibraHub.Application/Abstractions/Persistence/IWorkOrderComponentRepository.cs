using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// İş emri bileşen kayıtları (Faz 2 BOM patlatma).
/// </summary>
public interface IWorkOrderComponentRepository
{
    Task<IReadOnlyCollection<WorkOrderComponentDto>> GetByWorkOrderAsync(int workOrderId, CancellationToken ct);

    /// <summary>
    /// İş emrinin mevcut bileşenlerini sil ve yeni listeyi yaz (transactional, idempotent).
    /// </summary>
    Task ReplaceForWorkOrderAsync(int workOrderId, IReadOnlyCollection<WorkOrderComponent> components, CancellationToken ct);

    Task DeleteByWorkOrderAsync(int workOrderId, CancellationToken ct);

    /// <summary>
    /// Malzeme sarfı (2026-07-02) — WorkOrderComponent.IssuedQuantity += quantity VE
    /// DocumentLine'a Issue satırı AYNI transaction'da atomik yazılır (WorkOrder.DocumentId +
    /// WarehouseLocationId, component'in bağlı olduğu WorkOrder'dan JOIN ile çözülür).
    /// </summary>
    Task IssueAsync(int componentId, decimal quantity, int personnelId, CancellationToken ct);
}
