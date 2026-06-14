using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Üretim sahası aktivite log servisi — Faz 1 MVP (2026-05-20).
/// "Durum Değiştir" UI'sının arkasındaki davranış:
///   - StartAsync: yeni aktivite başlat (mevcut aktif varsa otomatik kapatır)
///   - EndCurrentAsync: aktif aktiviteyi kapatır (yeni aktivite başlatmadan — örn. operasyon biterken)
///   - GetActiveAsync / GetHistoryAsync: ShopFloor UI okuma yolu
/// </summary>
public interface IWorkOrderOperationActivityService
{
    Task<WorkOrderOperationActivityDto?> GetActiveAsync(int workOrderOperationId, CancellationToken ct);

    Task<IReadOnlyList<WorkOrderOperationActivityDto>> GetHistoryAsync(int workOrderOperationId, CancellationToken ct);

    /// <summary>
    /// Yeni aktivite başlatır. Mevcut aktif varsa önce kapatır (geçiş). DomainException
    /// (Other tipinde Notes zorunlu vs.) → ArgumentException olarak yansıtılır.
    /// </summary>
    Task<int> StartAsync(StartActivityRequest request, CancellationToken ct);

    /// <summary>
    /// Aktif aktiviteyi kapatır (yeni aktivite başlatmadan). Aktif yoksa false döner.
    /// </summary>
    Task<bool> EndCurrentAsync(EndActivityRequest request, CancellationToken ct);
}
