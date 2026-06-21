using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IApprovalInstanceRepository
{
    Task<ApprovalInstanceDto?> GetByDocumentIdAsync(int documentId, CancellationToken ct);
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
    Task UpdateRevisionIdAsync(int instanceId, int revisionId, CancellationToken ct);
    Task ApproveStepAsync(int instanceId, int stepOrder, string approverId, string approverName, string? note, CancellationToken ct);
    Task RejectAsync(int instanceId, int stepOrder, string approverId, string approverName, string note, CancellationToken ct);
    Task CancelAsync(int instanceId, string byUser, CancellationToken ct);

    /// <summary>
    /// Graph traversal yoluyla End node'a ulaşıldığında instance'ı tamamla.
    /// WHERE Status='Pending' koruması ile idempotent — normal ApproveStepAsync
    /// sonrası instance zaten Approved ise no-op olur.
    /// </summary>
    Task ForceCompleteAsync(int instanceId, CancellationToken ct);

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
    /// Graph döngü: step node'a geri dönüldüğünde SLA sıfırla (SlaActionAt/Type temizle, DueDate güncelle).
    /// nodeData'dan SLA süresi hesaplanır; SLA tanımlı değilse DueDate dokunulmaz.
    /// </summary>
    Task ResetSlaForLoopAsync(int instanceId, int stepOrder, string? nodeData, CancellationToken ct);

    /// <summary>
    /// Eskale: kaynak kaydi "Escalated" durumuna cek, ayni instance'a ayni step order ile
    /// yeni approver atanmis bir kayit yarat (SlaEscalatedFromRecordId = sourceRecordId).
    /// </summary>
    Task<int> CreateEscalatedStepAsync(int sourceRecordId, string newApproverId, string newApproverName, CancellationToken ct);

    Task<IReadOnlyList<ExtraColumnMetaDto>> GetViewColumnMetaAsync(string viewName, CancellationToken ct);

    /// <summary>
    /// Verilen instanceId'lere ait satir degerlerini view'dan ceker.
    /// Donus: instanceId → {kolonAdi → deger (string olarak)}.
    /// viewName whitelist'e gore dogrulanmalidir (controller sorumlu).
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyDictionary<string, string?>>> GetViewRowDataAsync(
        string viewName, IReadOnlyCollection<int> instanceIds, CancellationToken ct);
}
