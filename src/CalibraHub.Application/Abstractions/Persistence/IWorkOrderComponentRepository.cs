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
}
