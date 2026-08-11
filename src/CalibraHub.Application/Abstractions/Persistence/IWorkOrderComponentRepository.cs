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
    /// Tekil bileşen kaydı — sarf (Issue) öncesi eski durumu okumak (işlem logu) veya
    /// doğrulama için. Bulunamazsa null.
    /// </summary>
    Task<WorkOrderComponentDto?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// İş emrinin mevcut bileşenlerini sil ve yeni listeyi yaz (transactional, idempotent).
    /// </summary>
    Task ReplaceForWorkOrderAsync(int workOrderId, IReadOnlyCollection<WorkOrderComponent> components, CancellationToken ct);

    Task DeleteByWorkOrderAsync(int workOrderId, CancellationToken ct);

    /// <summary>
    /// Bileşenin planlı sarf lokasyonunu (FromLocationId) günceller — kullanıcı İş Emri
    /// ekranında ExplodeBom'un item-default önerisini elle değiştirebilir. locationId null
    /// ise kayıt NULL'a döner (sarf motoru IssueWorkOrderConsumptionAsync WO deposuna düşer).
    /// </summary>
    Task UpdateFromLocationAsync(int componentId, int? locationId, CancellationToken ct);

    /// <summary>
    /// Malzeme sarfı (2026-07-02) — WorkOrderComponent.IssuedQuantity += quantity VE
    /// DocumentLine'a Issue satırı AYNI transaction'da atomik yazılır (WorkOrder.DocumentId +
    /// WarehouseLocationId, component'in bağlı olduğu WorkOrder'dan JOIN ile çözülür).
    /// </summary>
    Task IssueAsync(int componentId, decimal quantity, int personnelId, CancellationToken ct);

    // ── İş emri bazında bileşen özelleştirme (2026-08-06, kullanıcı kararı: tam düzenleme) ──
    /// <summary>Tekil bileşen ekler (reçeteden bağımsız iş emri özelleştirmesi). Yeni Id döner.</summary>
    Task<int> AddAsync(WorkOrderComponent component, CancellationToken ct);
    /// <summary>Miktar/fire/not günceller. Sarf guard'ları SERVICE katmanında.</summary>
    Task UpdateAsync(int componentId, decimal requiredQuantity, decimal scrapRate, string? notes, CancellationToken ct);
    /// <summary>Tekil bileşen siler. Sarflı satır guard'ı SERVICE katmanında.</summary>
    Task DeleteAsync(int componentId, CancellationToken ct);
}
