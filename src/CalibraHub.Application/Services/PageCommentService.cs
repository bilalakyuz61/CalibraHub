using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services;

/// <summary>
/// IPageCommentService implementasyonu. IAuditTrailService opsiyonel ctor param olarak alınır
/// (CLAUDE.md "İşlem Log Modülü" kuralı — DI singleton, test/manuel constructor senaryolarında
/// null geçilebilir). PageCommentActivity yazımı ayrı, best-effort bir olay günlüğüdür — merkezi
/// audit trail'in yerine geçmez, ondan tamamen bağımsız çalışır ve asla ana akışı bozmaz.
/// </summary>
public sealed class PageCommentService : IPageCommentService
{
    private readonly IPageCommentRepository _repo;
    private readonly IAuditTrailService? _audit;
    private readonly ILogger<PageCommentService> _logger;

    public PageCommentService(
        IPageCommentRepository repo,
        ILogger<PageCommentService> logger,
        IAuditTrailService? audit = null)
    {
        _repo = repo;
        _logger = logger;
        _audit = audit;
    }

    public async Task<IReadOnlyList<PageCommentDto>> GetAllAsync(CancellationToken ct)
    {
        var comments = await _repo.ListAllAsync(ct);
        if (comments.Count == 0) return Array.Empty<PageCommentDto>();
        var revisions = await _repo.ListAllRevisionsAsync(ct);
        var images = await _repo.ListAllImageMetaAsync(ct);
        return Compose(comments, revisions, images);
    }

    public async Task<IReadOnlyList<PageCommentDto>> GetApprovedQueueAsync(CancellationToken ct)
    {
        var comments = await _repo.ListByStatusAsync("approved", ct);
        if (comments.Count == 0) return Array.Empty<PageCommentDto>();
        // Ajan kuyruğu revizyon geçmişini kullanmaz (tanım gereği hepsi henüz çözülmemiş) —
        // gereksiz sorgudan kaçınmak için revisions boş geçilir.
        var images = await _repo.ListAllImageMetaAsync(ct);
        return Compose(comments, Array.Empty<PageCommentRevision>(), images);
    }

    public async Task<PageCommentDto?> CreateAsync(CreatePageCommentInput input, string? createdBy, CancellationToken ct)
    {
        if (input is null ||
            string.IsNullOrWhiteSpace(input.Id) ||
            string.IsNullOrWhiteSpace(input.Key) ||
            string.IsNullOrWhiteSpace(input.Text))
            return null;

        var entity = new PageComment
        {
            Id = input.Id.Trim(),
            ScreenKey = input.Key.Trim(),
            PageTitle = string.IsNullOrWhiteSpace(input.PageTitle) ? null : input.PageTitle.Trim(),
            FormCode = string.IsNullOrWhiteSpace(input.FormCode) ? null : input.FormCode.Trim(),
            Text = input.Text,
            Author = string.IsNullOrWhiteSpace(input.Author) ? null : input.Author.Trim(),
            ClientCreatedAt = string.IsNullOrWhiteSpace(input.Ts) ? null : input.Ts,
            CreatedBy = createdBy,
        };

        var saved = await _repo.InsertAsync(entity, ct);

        // Insert audit — ilk değer dökümü (CLAUDE.md audit kuralı madde 3).
        _audit?.LogInsert("PageComment", saved.Id, BuildTitle(saved), snapshot: saved);

        await TryActivityAsync("comment.create", saved.ScreenKey,
            $"id={saved.Id}, author={saved.Author ?? "-"}", ct);

        var revisions = await _repo.ListRevisionsForCommentAsync(saved.Id, ct);
        var images = await _repo.ListImageMetaForCommentAsync(saved.Id, ct);
        return Compose(new[] { saved }, revisions, images)[0];
    }

    public async Task<PageCommentDto?> ChangeStatusAsync(
        ChangePageCommentStatusInput input, string? updatedBy, string activityAction,
        AuditActor? auditActorOverride, CancellationToken ct)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.Id) || string.IsNullOrWhiteSpace(input.Status))
            return null;

        // ID tabanlı eşleştirme (CLAUDE.md) — Key sözleşmede yer alır ama yalnızca bağlam/log
        // amaçlıdır; eşleştirme daima PK (Id) ile yapılır.
        var existing = await _repo.GetByIdAsync(input.Id.Trim(), ct);
        if (existing is null) return null;

        var before = CloneComment(existing);

        var nowIso = DateTimeOffset.UtcNow.ToString("o");
        existing.Status = input.Status.Trim();
        existing.AppliedVersion = string.IsNullOrWhiteSpace(input.Version) ? null : input.Version.Trim();
        existing.ResolvedBy = string.IsNullOrWhiteSpace(input.User) ? null : input.User.Trim();
        existing.ResolvedAt = nowIso;
        existing.DevNote = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note;
        existing.UpdatedBy = updatedBy;

        await _repo.UpdateStatusAsync(existing, ct);

        await _repo.AddRevisionAsync(new PageCommentRevision
        {
            CommentId = existing.Id,
            Status = existing.Status,
            Version = existing.AppliedVersion,
            Note = existing.DevNote,
            UserName = existing.ResolvedBy,
            ClientCreatedAt = nowIso,
        }, ct);

        _audit?.LogUpdate("PageComment", existing.Id, BuildTitle(existing), before, existing, actor: auditActorOverride);

        await TryActivityAsync(activityAction, existing.ScreenKey,
            $"id={existing.Id}, status={existing.Status}, version={existing.AppliedVersion ?? "-"}, user={existing.ResolvedBy ?? "-"}",
            ct);

        var revisions = await _repo.ListRevisionsForCommentAsync(existing.Id, ct);
        var images = await _repo.ListImageMetaForCommentAsync(existing.Id, ct);
        return Compose(new[] { existing }, revisions, images)[0];
    }

    public async Task<PageCommentDto?> EditTextAsync(EditPageCommentTextInput input, string? updatedBy, CancellationToken ct)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.Id) || string.IsNullOrWhiteSpace(input.Text))
            return null;

        var existing = await _repo.GetByIdAsync(input.Id.Trim(), ct);
        if (existing is null) return null;

        var before = CloneComment(existing);
        existing.Text = input.Text;
        existing.UpdatedBy = updatedBy;

        await _repo.UpdateTextAsync(existing.Id, existing.Text, updatedBy, ct);

        _audit?.LogUpdate("PageComment", existing.Id, BuildTitle(existing), before, existing);

        await TryActivityAsync("comment.edit", existing.ScreenKey,
            $"id={existing.Id}, user={input.User ?? "-"}", ct);

        var revisions = await _repo.ListRevisionsForCommentAsync(existing.Id, ct);
        var images = await _repo.ListImageMetaForCommentAsync(existing.Id, ct);
        return Compose(new[] { existing }, revisions, images)[0];
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        var existing = await _repo.GetByIdAsync(id.Trim(), ct);
        if (existing is null) return false;

        // Silinen içeriğin dökümü (CLAUDE.md audit kuralı madde 3 — "ne kayboldu" izlenebilir).
        var snapshot = new List<AuditFieldChange>
        {
            new("Text", "Metin", existing.Text, null),
            new("ScreenKey", "Ekran Anahtarı", existing.ScreenKey, null),
            new("Status", "Durum", existing.Status, null),
        };

        var deleted = await _repo.DeleteAsync(existing.Id, ct);
        if (!deleted) return false;

        _audit?.LogDelete("PageComment", existing.Id, BuildTitle(existing), snapshot: snapshot);

        await TryActivityAsync("comment.delete", existing.ScreenKey, $"id={existing.Id}", ct);
        return true;
    }

    public async Task<PageCommentImageMetaDto?> AddImageAsync(AddPageCommentImageInput input, CancellationToken ct)
    {
        if (input is null ||
            string.IsNullOrWhiteSpace(input.Id) ||
            string.IsNullOrWhiteSpace(input.CommentId) ||
            string.IsNullOrWhiteSpace(input.DataBase64))
            return null;

        var owner = await _repo.GetByIdAsync(input.CommentId.Trim(), ct);
        if (owner is null) return null;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(StripDataUriPrefix(input.DataBase64));
        }
        catch (FormatException)
        {
            return null;
        }
        if (bytes.Length == 0) return null;

        var image = new PageCommentImage
        {
            Id = input.Id.Trim(),
            CommentId = owner.Id,
            Mime = string.IsNullOrWhiteSpace(input.Mime) ? "application/octet-stream" : input.Mime.Trim(),
            Name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name.Trim(),
            Bytes = bytes,
            ClientCreatedAt = DateTimeOffset.UtcNow.ToString("o"),
        };

        await _repo.AddImageAsync(image, ct);

        await TryActivityAsync("comment.image.add", owner.ScreenKey,
            $"commentId={owner.Id}, imageId={image.Id}, bytes={bytes.Length}", ct);

        return new PageCommentImageMetaDto { Id = image.Id, Mime = image.Mime, Name = image.Name };
    }

    public async Task<(byte[] Bytes, string Mime)?> GetImageBinaryAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var image = await _repo.GetImageBinaryAsync(id.Trim(), ct);
        if (image?.Bytes is not { Length: > 0 }) return null;
        return (image.Bytes, string.IsNullOrWhiteSpace(image.Mime) ? "application/octet-stream" : image.Mime);
    }

    public async Task<bool> DeleteImageAsync(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        var trimmed = id.Trim();
        var deleted = await _repo.DeleteImageAsync(trimmed, ct);
        if (deleted)
            await TryActivityAsync("comment.image.delete", null, $"imageId={trimmed}", ct);
        return deleted;
    }

    public async Task<IReadOnlyList<PageCommentActivityDto>> GetActivityAsync(int take, CancellationToken ct)
    {
        var capped = take <= 0 ? 300 : Math.Min(take, 1000);
        var rows = await _repo.ListActivityAsync(capped, ct);
        return rows.Select(a => new PageCommentActivityDto
        {
            Id = a.Id,
            Action = a.Action,
            ScreenKey = a.ScreenKey,
            Detail = a.Detail,
            Ts = a.ClientCreatedAt,
            Created = a.Created,
        }).ToList();
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────

    /// <summary>PageCommentActivity yazımı best-effort'tur — hata ana akışı asla bozmaz.</summary>
    private async Task TryActivityAsync(string action, string? screenKey, string? detail, CancellationToken ct)
    {
        try
        {
            await _repo.AddActivityAsync(new PageCommentActivity
            {
                Action = action,
                ScreenKey = screenKey,
                Detail = detail,
                ClientCreatedAt = DateTimeOffset.UtcNow.ToString("o"),
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PageComments] Activity log yazılamadı: {Action}", action);
        }
    }

    /// <summary>"data:image/png;base64,AAAA..." → yalnızca base64 gövdesi; ham base64 ise aynen döner.</summary>
    private static string StripDataUriPrefix(string data)
    {
        var comma = data.IndexOf(',');
        return comma >= 0 && data.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? data[(comma + 1)..]
            : data;
    }

    private static string BuildTitle(PageComment c) =>
        string.IsNullOrWhiteSpace(c.PageTitle) ? c.ScreenKey : $"{c.PageTitle} ({c.ScreenKey})";

    /// <summary>LogUpdate reflection diff'i için mutasyon ÖNCESİ anlık görüntü (CLAUDE.md audit kuralı).</summary>
    private static PageComment CloneComment(PageComment c) => new()
    {
        Id = c.Id,
        Seq = c.Seq,
        ScreenKey = c.ScreenKey,
        PageTitle = c.PageTitle,
        FormCode = c.FormCode,
        Text = c.Text,
        Author = c.Author,
        Status = c.Status,
        AppliedVersion = c.AppliedVersion,
        ResolvedBy = c.ResolvedBy,
        ResolvedAt = c.ResolvedAt,
        DevNote = c.DevNote,
        ClientCreatedAt = c.ClientCreatedAt,
        Created = c.Created,
        CreatedBy = c.CreatedBy,
        Updated = c.Updated,
        UpdatedBy = c.UpdatedBy,
    };

    private static List<PageCommentDto> Compose(
        IReadOnlyList<PageComment> comments,
        IReadOnlyList<PageCommentRevision> revisions,
        IReadOnlyList<PageCommentImage> images)
    {
        var revByComment = revisions
            .GroupBy(r => r.CommentId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var imgByComment = images
            .GroupBy(i => i.CommentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<PageCommentDto>(comments.Count);
        foreach (var c in comments)
        {
            var dto = ToDto(c);
            if (revByComment.TryGetValue(c.Id, out var revs))
                dto.Revisions = revs.Select(ToDto).ToList();
            if (imgByComment.TryGetValue(c.Id, out var imgs))
                dto.Images = imgs.Select(ToDto).ToList();
            result.Add(dto);
        }
        return result;
    }

    private static PageCommentDto ToDto(PageComment c) => new()
    {
        Id = c.Id,
        Seq = c.Seq,
        ScreenKey = c.ScreenKey,
        PageTitle = c.PageTitle,
        FormCode = c.FormCode,
        Text = c.Text,
        Ts = c.ClientCreatedAt,
        Author = c.Author,
        Status = c.Status,
        AppliedVersion = c.AppliedVersion,
        ResolvedBy = c.ResolvedBy,
        ResolvedAt = c.ResolvedAt,
        DevNote = c.DevNote,
    };

    private static PageCommentRevisionDto ToDto(PageCommentRevision r) => new()
    {
        Status = r.Status,
        Version = r.Version,
        Note = r.Note,
        UserName = r.UserName,
        CreatedAt = r.ClientCreatedAt,
    };

    private static PageCommentImageMetaDto ToDto(PageCommentImage i) => new()
    {
        Id = i.Id,
        Mime = i.Mime,
        Name = i.Name,
    };
}
