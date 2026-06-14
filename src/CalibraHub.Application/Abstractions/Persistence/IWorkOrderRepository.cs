using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Uretim is emri persistence — WorkOrder ve WorkOrderSource tablolari.
/// List/Get DTO doner (JOIN'li display alanlari icin); CRUD entity tabanli.
/// </summary>
public interface IWorkOrderRepository
{
    Task<IReadOnlyCollection<WorkOrderListItemDto>> ListAsync(WorkOrderStatus? status, CancellationToken ct);

    Task<WorkOrderDto?> GetAsync(int id, CancellationToken ct);

    Task<int> CreateAsync(WorkOrder entity, CancellationToken ct);

    Task UpdateAsync(int id, UpdateWorkOrderRequest req, int? updatedBy, CancellationToken ct);

    Task ChangeStatusAsync(int id, WorkOrderStatus newStatus, int? userId, CancellationToken ct);

    /// <summary>Released sonrasi revize: yeni emir kopyalanir (RevisionNo+1, RevisedFromId=eski), eski Cancelled.</summary>
    Task<int> CreateRevisionAsync(int existingId, int? userId, CancellationToken ct);

    Task<IReadOnlyCollection<WorkOrderSourceDto>> GetSourcesAsync(int workOrderId, CancellationToken ct);

    Task AddSourceAsync(int workOrderId, int sourceDocumentId, int sourceLineId, decimal allocatedQuantity, CancellationToken ct);

    /// <summary>Bir DocumentLine icin atanmis toplam miktar — kalan açik miktar hesaplama icin.</summary>
    Task<decimal> GetAllocatedQuantityForLineAsync(int sourceLineId, CancellationToken ct);

    /// <summary>Toplama (mevcut emire ekleme) icin uygun is emirleri: ayni Item+Config, Status IN (Planned, Released).</summary>
    Task<IReadOnlyCollection<WorkOrderListItemDto>> ListEligibleForMergeAsync(int itemId, int? configId, CancellationToken ct);

    /// <summary>İş emrinin RoutingId alanını günceller (Release auto-resolve sırasında kullanılır).</summary>
    Task SetRoutingIdAsync(int workOrderId, int routingId, CancellationToken ct);

    /// <summary>Item için aktif Routing arar (öncelik: ConfigId match → ConfigId NULL fallback). Yoksa NULL.</summary>
    Task<int?> FindRoutingForItemAsync(int itemId, int? configId, CancellationToken ct);
}
