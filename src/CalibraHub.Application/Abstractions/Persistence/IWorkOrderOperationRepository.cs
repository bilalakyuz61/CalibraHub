using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IWorkOrderOperationRepository
{
    /// <summary>Bir iş emrinin tüm operasyon adımları (sequence sıralı, JOIN'lı DTO).</summary>
    Task<IReadOnlyCollection<WorkOrderOperationDto>> GetByWorkOrderAsync(int workOrderId, CancellationToken ct);

    /// <summary>Belirli bir makinede bekleyen/devam eden operasyonlar (shop-floor kuyruk).</summary>
    Task<IReadOnlyCollection<WorkOrderOperationDto>> GetQueueByMachineAsync(int machineId, CancellationToken ct);

    Task<WorkOrderOperationDto?> GetAsync(int id, CancellationToken ct);

    /// <summary>UPSERT: Id=0 yeni, Id>0 update. Yalnızca tanım alanları (Sequence/OperationId/MachineId/Süre/Notlar).</summary>
    Task<int> SaveAsync(WorkOrderOperation entity, CancellationToken ct);

    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>Auto-explosion: routing'in operasyonlarını verilen iş emrine kopyalar (transactional).</summary>
    Task ExplodeFromRoutingAsync(int workOrderId, int routingId, CancellationToken ct);

    /// <summary>Status + StartedBy/At alanlarını günceller (Başlat aksiyonu). personnelId = Personnel.Id.</summary>
    Task StartAsync(int id, int personnelId, CancellationToken ct);

    /// <summary>ProducedQuantity += qty, ScrapQuantity += scrap. Status InProgress'e geçer (henüz Pending ise).</summary>
    Task PartialCompleteAsync(int id, int personnelId, decimal quantity, decimal? scrap, CancellationToken ct);

    /// <summary>
    /// ProducedQuantity = finalQty (verilmişse), Status=Completed, CompletedBy/At doldurulur.
    /// stockLine verilmişse (son operasyon tamamlanıyor ve mamul girişi yazılması gerekiyorsa)
    /// AYNI transaction'da DocumentLine'a append edilir — WorkOrderOperationService bu
    /// koordinasyonu Application katmanında yapamaz (SqlConnection'a erişimi yok), bu yüzden
    /// atomiklik repository içinde sağlanır ("operasyon tamamlandı ama stok hiç yazılmadı" riskini önler).
    /// </summary>
    Task CompleteAsync(int id, int personnelId, decimal? finalQuantity, DocumentLine? stockLine, CancellationToken ct);
}
