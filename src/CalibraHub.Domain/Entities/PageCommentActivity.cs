using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Sayfa-içi Yorum sisteminin kendi olay günlüğü (özelliğe özel) — merkezi işlem log
/// (<c>IAuditTrailService</c> / dosya tabanlı audit trail) sisteminden AYRIDIR. Her mutasyon
/// (create/status/edit/delete/image add/image delete) burada bir satır bırakır; kullanıcı
/// kimliği için ayrı bir kolon yoktur — gerekiyorsa <see cref="Detail"/> serbest metnine gömülür.
/// </summary>
public sealed class PageCommentActivity : EntityInt
{
    /// <summary>Ör. "comment.create", "comment.status", "comment.agent_apply", "comment.delete".</summary>
    public string Action { get; set; } = string.Empty;

    public string? ScreenKey { get; set; }
    public string? Detail { get; set; }
    public string? ClientCreatedAt { get; set; }
    public DateTime Created { get; set; }
}
