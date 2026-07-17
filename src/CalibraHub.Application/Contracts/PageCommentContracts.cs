namespace CalibraHub.Application.Contracts;

// ── Sayfa-içi Yorum sistemi — yanıt DTO'ları ────────────────────────────────
// Alan adları PageCommentsController'da camelCase JSON'a birebir map edilir
// (bkz. ToCommentJson). Ts = PageComment.ClientCreatedAt; CreatedAt (revizyon) =
// PageCommentRevision.ClientCreatedAt.

public sealed class PageCommentDto
{
    public string Id { get; set; } = string.Empty;
    public int Seq { get; set; }
    public string ScreenKey { get; set; } = string.Empty;
    public string? PageTitle { get; set; }
    public string? FormCode { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Ts { get; set; }
    public string? Author { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AppliedVersion { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolvedAt { get; set; }
    public string? DevNote { get; set; }
    public List<PageCommentRevisionDto> Revisions { get; set; } = new();
    public List<PageCommentImageMetaDto> Images { get; set; } = new();
}

public sealed class PageCommentRevisionDto
{
    public string? Status { get; set; }
    public string? Version { get; set; }
    public string? Note { get; set; }
    public string? UserName { get; set; }
    public string? CreatedAt { get; set; }
}

public sealed class PageCommentImageMetaDto
{
    public string Id { get; set; } = string.Empty;
    public string? Mime { get; set; }
    public string? Name { get; set; }
}

public sealed class PageCommentActivityDto
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? ScreenKey { get; set; }
    public string? Detail { get; set; }
    public string? Ts { get; set; }
    public DateTime Created { get; set; }
}

// ── Sayfa-içi Yorum sistemi — servis giriş modelleri (controller [FromBody] bunlara bağlanır) ──

public sealed class CreatePageCommentInput
{
    public string Key { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Ts { get; set; }
    public string? Author { get; set; }
    public string? PageTitle { get; set; }
    public string? FormCode { get; set; }
}

/// <summary>
/// Hem kullanıcı <c>comment/status</c> hem Katman-4 <c>agent-apply</c> endpoint'i bu modeli kullanır.
/// Agent-apply body'sinde <see cref="Key"/> gönderilmez — boş kalır, servis yalnızca Id (PK) ile eşleştirir
/// (CLAUDE.md "ID tabanlı eşleştirme" kuralı; Key sadece bağlam/log amaçlıdır).
/// </summary>
public sealed class ChangePageCommentStatusInput
{
    public string Key { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Note { get; set; }
    public string? User { get; set; }
}

public sealed class EditPageCommentTextInput
{
    public string Key { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? User { get; set; }
}

public sealed class DeletePageCommentInput
{
    public string Key { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}

public sealed class AddPageCommentImageInput
{
    public string CommentId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string? Mime { get; set; }
    public string DataBase64 { get; set; } = string.Empty;
    public string? Name { get; set; }
}

public sealed class DeletePageCommentImageInput
{
    public string Id { get; set; } = string.Empty;
}
