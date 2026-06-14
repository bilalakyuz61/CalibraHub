using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IApprovalInstanceRepository
{
    Task<ApprovalInstanceDto?> GetByDocumentIdAsync(Guid documentId, CancellationToken ct);
    Task<ApprovalInstanceDto?> GetByIdAsync(int instanceId, CancellationToken ct);
    Task<IReadOnlyList<ApprovalInstanceDto>> GetPendingAsync(CancellationToken ct);

    /// <summary>
    /// Onayda bekleyen step kayitlari — kullaniciya gore scope'lanmis.
    /// scope: "mine" → ApproverId = userId, "department" → kullanicinin departmanindaki userId'ler,
    ///        "all" → tum pending (admin).
    /// userId = ApprovalStepRecord.ApproverId ile karsilastirilir (string).
    /// departmentUserIds = department scope icin pre-resolved user kimlikleri (null = filtre yok).
    /// </summary>
    Task<IReadOnlyList<PendingApprovalItemDto>> GetPendingForUserAsync(
        string userId,
        string scope,
        IReadOnlyCollection<string>? departmentUserIds,
        CancellationToken ct);

    /// <summary>Tek bir instance icin tum step kayitlarini doner (detail modal).</summary>
    Task<PendingApprovalDetailDto?> GetPendingDetailAsync(int instanceId, CancellationToken ct);
    Task<int> CreateAsync(StartApprovalRequest request, IReadOnlyList<ApprovalFlowStepDto> steps, CancellationToken ct);
    Task ApproveStepAsync(int instanceId, int stepOrder, string approverId, string approverName, string? note, CancellationToken ct);
    Task RejectAsync(int instanceId, int stepOrder, string approverId, string approverName, string note, CancellationToken ct);
    Task CancelAsync(int instanceId, string byUser, CancellationToken ct);

    // ── SLA islemleri ─────────────────────────────────────────────────────────
    /// <summary>DueDate gecmis, SLA aksiyonu henuz uygulanmamis Pending kayitlar.</summary>
    Task<IReadOnlyList<OverdueStepRecord>> GetOverdueStepsAsync(DateTime nowUtc, CancellationToken ct);

    /// <summary>DueDate yaklasan ama henuz uyarilmamis Pending kayitlar (pre-warning).</summary>
    Task<IReadOnlyList<OverdueStepRecord>> GetPendingWarningsAsync(DateTime nowUtc, CancellationToken ct);

    /// <summary>SLA aksiyonu uygulandi olarak isaretle (SlaActionAt + SlaActionType).</summary>
    Task MarkSlaActionAsync(int recordId, string actionType, CancellationToken ct);

    /// <summary>Pre-warning gonderildi olarak isaretle (SlaWarnedAt).</summary>
    Task MarkSlaWarnedAsync(int recordId, CancellationToken ct);

    /// <summary>
    /// Eskale: kaynak kaydi "Escalated" durumuna cek, ayni instance'a ayni step order ile
    /// yeni approver atanmis bir kayit yarat (SlaEscalatedFromRecordId = sourceRecordId).
    /// </summary>
    Task<int> CreateEscalatedStepAsync(int sourceRecordId, string newApproverId, string newApproverName, CancellationToken ct);
}
