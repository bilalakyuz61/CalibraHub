using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Üretim sahası aktivite log repository — Faz 1 MVP (2026-05-20).
/// Her WorkOrderOperation üzerinde yapılan eylemler ayrı satır olarak saklanır.
/// Aynı operasyonda anda yalnız bir 'aktif' (EndedAt NULL) satır olur (filtered unique
/// index garantiler; service yeni aktivite başlatmadan önce mevcut aktifi kapatır).
/// </summary>
public interface IWorkOrderOperationActivityRepository
{
    /// <summary>Operasyon için aktif (EndedAt NULL) aktivite — yoksa null.</summary>
    Task<WorkOrderOperationActivityDto?> GetActiveAsync(int workOrderOperationId, CancellationToken ct);

    /// <summary>Operasyonun tüm hareket geçmişi (StartedAt DESC, JOIN'lı Personnel adı).</summary>
    Task<IReadOnlyList<WorkOrderOperationActivityDto>> GetHistoryAsync(int workOrderOperationId, CancellationToken ct);

    /// <summary>
    /// Aktif aktiviteyi kapatır (EndedAt = SYSUTCDATETIME, opsiyonel Notes).
    /// Aktif yoksa no-op (false döner). Service yeni aktivite başlatmadan önce çağırır.
    /// </summary>
    Task<bool> EndActiveAsync(int workOrderOperationId, int personnelId, string? notes, CancellationToken ct);

    /// <summary>Yeni aktivite satırı yaratır (INSERT). Caller önce EndActiveAsync ile mevcut aktifi kapatmalı.</summary>
    Task<int> StartAsync(WorkOrderOperationActivity activity, CancellationToken ct);
}
