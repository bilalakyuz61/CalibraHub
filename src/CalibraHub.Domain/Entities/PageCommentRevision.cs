using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir <see cref="PageComment"/> üzerindeki durum geçişi geçmişi (status/version/note/user).
/// Her <c>comment/status</c> ve <c>agent-apply</c> çağrısında bir satır eklenir — geçmiş asla
/// güncellenmez/silinmez (append-only). CommentId FK CASCADE — yorum silinince revizyonlar da gider.
/// </summary>
public sealed class PageCommentRevision : EntityInt
{
    public string CommentId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Version { get; set; }
    public string? Note { get; set; }
    public string? UserName { get; set; }

    /// <summary>ISO 8601 string — istemci sağlamazsa sunucu UtcNow ile doldurur.</summary>
    public string? ClientCreatedAt { get; set; }

    public DateTime Created { get; set; }
}
