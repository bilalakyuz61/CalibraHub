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

    /// <summary>
    /// Released sonrasi revize: yeni WorkOrder satiri kopyalanir (newDocumentId ile — Document
    /// tarafindaki yeni revizyon satirini SERVICE onceden olusturur ve buraya parametre gecer),
    /// eski WorkOrder Cancelled olur.
    /// </summary>
    Task<int> CreateRevisionAsync(int existingId, int newDocumentId, int? userId, CancellationToken ct);

    Task<IReadOnlyCollection<WorkOrderSourceDto>> GetSourcesAsync(int workOrderId, CancellationToken ct);

    Task AddSourceAsync(int workOrderId, int sourceDocumentId, int sourceLineId, decimal allocatedQuantity, CancellationToken ct);

    /// <summary>Bir DocumentLine icin atanmis toplam miktar — kalan açik miktar hesaplama icin.</summary>
    Task<decimal> GetAllocatedQuantityForLineAsync(int sourceLineId, CancellationToken ct);

    /// <summary>
    /// 2026-07-20 — Bir kaynak belgenin (ör. satış siparişi) TÜM satırları için WorkOrderSource
    /// tahsis toplamı, tek sorguda: sourceLineId → toplam AllocatedQuantity. Yalnızca aktif iş
    /// emirleri sayılır (Status &lt;&gt; Cancelled, IsActive=1) — <see cref="GetAllocatedQuantityForLineAsync"/>
    /// ile aynı filtre, TOPLU (N+1'siz) sürüm. DocumentService'in miktar guard'ları
    /// (SaveQuoteAsync/GetLineLocksAsync/GetDeleteBlockReasonAsync) belge başına tek çağrıyla
    /// kullanır. Tahsisi olmayan belge için boş sözlük döner.
    /// </summary>
    Task<IReadOnlyDictionary<int, decimal>> GetAllocatedQuantitiesForDocumentAsync(int sourceDocumentId, CancellationToken ct);

    /// <summary>Toplama (mevcut emire ekleme) icin uygun is emirleri: ayni Item+Config, Status IN (Planned, Released).</summary>
    Task<IReadOnlyCollection<WorkOrderListItemDto>> ListEligibleForMergeAsync(int itemId, int? configId, CancellationToken ct);

    /// <summary>İş emrinin RoutingId alanını günceller (Release auto-resolve sırasında kullanılır).</summary>
    Task SetRoutingIdAsync(int workOrderId, int routingId, CancellationToken ct);

    /// <summary>Item için aktif Routing arar (öncelik: ConfigId match → ConfigId NULL fallback). Yoksa NULL.</summary>
    Task<int?> FindRoutingForItemAsync(int itemId, int? configId, CancellationToken ct);
}
