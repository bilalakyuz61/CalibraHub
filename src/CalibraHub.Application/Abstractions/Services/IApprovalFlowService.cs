using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IApprovalFlowService
{
    Task<IReadOnlyList<ApprovalFlowSummaryDto>> GetAllAsync(CancellationToken ct);
    Task<ApprovalFlowDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<int> SaveAsync(SaveApprovalFlowRequest request, int? byUserId, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>
    /// Mevcut akışı kopyalar — name'e " (Kopya)" eklenir, IsActive=false (kopya yanlışlıkla
    /// devreye girmesin) ve tüm step/rule/edge id'leri sıfırlanır. Yeni id döner.
    /// </summary>
    Task<int> DuplicateAsync(int sourceId, int? byUserId, CancellationToken ct);

    // Kural motoru: belge tipi + tutar + VKN + departman'a göre en yüksek öncelikli akışı döner.
    // departmentId null gönderildiyse Department kuralı uygulanmış akışlar bu belgeye eşlemez.
    Task<ApprovalFlowDto?> MatchFlowAsync(string documentKind, decimal? totalAmount, string? senderTaxNo, int? departmentId, CancellationToken ct);

    /// <summary>
    /// Bu belge türü (kind) için TANIMLI ve aktif bir onay akışı var mı — TUTAR/VKN/departman
    /// KURALLARINA BAKMAKSIZIN. Elle "Onaylandı/Reddedildi" durum kilidinin (hibrit governance)
    /// tip-seviyesi sinyali. MatchFlowAsync KULLANILMAZ: o, null tutarda tutar-kurallı akışları
    /// eşleştirmez (FlowMatchesRules) → yalnız-tutar-kurallı bir akış varsa "yok" der ve kilit delinir.
    /// </summary>
    Task<bool> HasActiveFlowForKindAsync(string documentKind, CancellationToken ct);

    // İşleme alma ve adım onay/red
    Task<ApprovalInstanceDto> StartAsync(StartApprovalRequest request, CancellationToken ct);
    Task<ApprovalInstanceDto> ApproveStepAsync(ApproveStepRequest request, CancellationToken ct);
    Task<ApprovalInstanceDto> RejectAsync(RejectStepRequest request, CancellationToken ct);
    Task<ApprovalInstanceDto> CancelAsync(int instanceId, string byUser, CancellationToken ct);

    Task<ApprovalInstanceDto?> GetInstanceByDocumentIdAsync(int documentId, CancellationToken ct);

    // Execution log — her node traversal + onay/red/iptal olayları
    Task<IReadOnlyList<ApprovalNodeLogDto>> GetInstanceLogsAsync(int instanceId, CancellationToken ct);

    // Akış revizyon geçmişi
    Task<IReadOnlyList<ApprovalFlowRevisionSummaryDto>> GetRevisionsAsync(int flowId, CancellationToken ct);
    Task<ApprovalFlowRevisionDetailDto?> GetRevisionDetailAsync(int revisionId, CancellationToken ct);
}
