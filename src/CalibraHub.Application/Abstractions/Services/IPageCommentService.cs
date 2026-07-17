using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Sayfa-içi Yorum sistemi — orkestrasyon katmanı (repository çağrıları + PageCommentActivity
/// olay günlüğü + merkezi İşlem Log (IAuditTrailService) enstrümantasyonu). Hem admin JSON API
/// (PageCommentsController kullanıcı grubu) hem Katman-4 ajan endpoint'leri (agent-queue/agent-apply)
/// aynı servisi paylaşır — durum değişikliği mantığı (<see cref="ChangeStatusAsync"/>) tek yerdedir.
/// </summary>
public interface IPageCommentService
{
    /// <summary>Tüm yorumlar (her durumdan), Seq ASC — kullanıcı GET /api/page-comments için.</summary>
    Task<IReadOnlyList<PageCommentDto>> GetAllAsync(CancellationToken ct);

    /// <summary>Status='approved' yorumlar, Seq ASC — Katman-4 agent-queue için (revisions boş döner, gerekmiyor).</summary>
    Task<IReadOnlyList<PageCommentDto>> GetApprovedQueueAsync(CancellationToken ct);

    /// <summary>Geçersiz girdi (key/id/text boş) → null.</summary>
    Task<PageCommentDto?> CreateAsync(CreatePageCommentInput input, string? createdBy, CancellationToken ct);

    /// <summary>
    /// Durum değişikliği + PageCommentRevision satırı + PageCommentActivity + merkezi audit (LogUpdate).
    /// <paramref name="activityAction"/> çağıran taraf belirler (ör. "comment.status" | "comment.agent_apply") —
    /// aynı mantık iki farklı giriş noktasından (kullanıcı/ajan) tetiklenebildiği için ayırt edilebilir kalır.
    /// <paramref name="auditActorOverride"/> anonim (Katman-4) çağrılarda HTTP context'ten CompanyId
    /// çözümlenemediği için Controller tarafından açıkça verilir; kullanıcı akışında null bırakılır
    /// (ambient HTTP context otomatik çözer). Yorum bulunamazsa null.
    /// </summary>
    Task<PageCommentDto?> ChangeStatusAsync(
        ChangePageCommentStatusInput input, string? updatedBy, string activityAction,
        AuditActor? auditActorOverride, CancellationToken ct);

    /// <summary>Yorum bulunamazsa null.</summary>
    Task<PageCommentDto?> EditTextAsync(EditPageCommentTextInput input, string? updatedBy, CancellationToken ct);

    Task<bool> DeleteAsync(string id, CancellationToken ct);

    /// <summary>Base64 decode edilemezse veya sahibi (CommentId) yoksa null.</summary>
    Task<PageCommentImageMetaDto?> AddImageAsync(AddPageCommentImageInput input, CancellationToken ct);

    Task<(byte[] Bytes, string Mime)?> GetImageBinaryAsync(string id, CancellationToken ct);

    Task<bool> DeleteImageAsync(string id, CancellationToken ct);

    /// <summary>Son N aktivite (Created DESC).</summary>
    Task<IReadOnlyList<PageCommentActivityDto>> GetActivityAsync(int take, CancellationToken ct);
}
