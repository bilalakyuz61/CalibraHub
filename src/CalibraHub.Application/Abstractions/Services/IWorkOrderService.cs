using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Services;

public interface IWorkOrderService
{
    Task<IReadOnlyCollection<WorkOrderListItemDto>> ListAsync(WorkOrderStatus? status, CancellationToken ct);

    Task<WorkOrderDto?> GetAsync(int id, CancellationToken ct);

    Task<int> CreateAsync(CreateWorkOrderRequest request, CancellationToken ct);

    Task UpdateAsync(int id, UpdateWorkOrderRequest request, CancellationToken ct);

    Task ChangeStatusAsync(int id, WorkOrderStatus newStatus, CancellationToken ct);

    /// <summary>Released sonrasi degisiklik akisi — yeni revizyon emir doner.</summary>
    Task<int> ReviseAsync(int id, CancellationToken ct);

    /// <summary>
    /// Sales order satirindan is emri uretir. TargetWorkOrderId NULL ise yeni emir,
    /// dolu ise mevcut emire allocation ekler (toplama). Bolme: ayni satirdan birden fazla
    /// emir cagri ile parcali.
    /// </summary>
    Task<int> CreateFromSalesLineAsync(CreateWorkOrderFromSalesLineRequest request, CancellationToken ct);

    Task<IReadOnlyCollection<WorkOrderListItemDto>> ListEligibleForMergeAsync(int itemId, int? configId, CancellationToken ct);

    /// <summary>Bir sipariş satırı için atanmış toplam miktar (açık bakiye hesabı).</summary>
    Task<decimal> GetAllocatedQuantityForLineAsync(int sourceLineId, CancellationToken ct);

    /// <summary>
    /// Faz 2 — İş emrinin reçetesini patlatır. Her bileşen için
    /// <c>RequiredQty = bomLine.Quantity × wo.PlannedQuantity × (1 + ScrapRatio)</c>
    /// hesaplanır ve WorkOrderComponent tablosuna yazılır (idempotent re-explode).
    /// </summary>
    Task<ExplodeBomResultDto> ExplodeBomAsync(int workOrderId, CancellationToken ct);

    /// <summary>İş emrinin patlatılmış bileşen listesi.</summary>
    Task<IReadOnlyCollection<WorkOrderComponentDto>> GetComponentsAsync(int workOrderId, CancellationToken ct);
}
