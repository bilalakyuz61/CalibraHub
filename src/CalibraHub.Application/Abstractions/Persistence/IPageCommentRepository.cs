using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Sayfa-içi Yorum sistemi — ham DB erişimi. Per-company DB'de yaşar; bağlantı
/// SqlServerConnectionFactory ile per-request çözümlenir (CompanyId kolonu yoktur).
/// Şema pc-db tarafından kurulur (PageComment/PageCommentRevision/PageCommentImage/
/// PageCommentActivity) — bu repository migration YAZMAZ, yalnızca kodlar.
/// </summary>
public interface IPageCommentRepository
{
    /// <summary>Tüm yorumlar, Seq ASC.</summary>
    Task<IReadOnlyList<PageComment>> ListAllAsync(CancellationToken ct);

    /// <summary>Belirli durumdaki yorumlar (ör. "approved" — Katman-4 kuyruğu), Seq ASC.</summary>
    Task<IReadOnlyList<PageComment>> ListByStatusAsync(string status, CancellationToken ct);

    Task<PageComment?> GetByIdAsync(string id, CancellationToken ct);

    /// <summary>Ekler; dönen nesnede sunucunun ürettiği Seq/Status(DEFAULT)/Created doldurulmuş olur.</summary>
    Task<PageComment> InsertAsync(PageComment comment, CancellationToken ct);

    /// <summary>Status/AppliedVersion/ResolvedBy/ResolvedAt/DevNote/UpdatedBy günceller (Updated=SYSUTCDATETIME).</summary>
    Task<bool> UpdateStatusAsync(PageComment comment, CancellationToken ct);

    Task<bool> UpdateTextAsync(string id, string text, string? updatedBy, CancellationToken ct);

    /// <summary>CASCADE ile Revision + Image satırları da silinir.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct);

    Task AddRevisionAsync(PageCommentRevision revision, CancellationToken ct);
    Task<IReadOnlyList<PageCommentRevision>> ListAllRevisionsAsync(CancellationToken ct);
    Task<IReadOnlyList<PageCommentRevision>> ListRevisionsForCommentAsync(string commentId, CancellationToken ct);

    Task AddImageAsync(PageCommentImage image, CancellationToken ct);

    /// <summary>Bytes hariç metadata (Id/Mime/Name) — tüm yorumlar için.</summary>
    Task<IReadOnlyList<PageCommentImage>> ListAllImageMetaAsync(CancellationToken ct);

    Task<IReadOnlyList<PageCommentImage>> ListImageMetaForCommentAsync(string commentId, CancellationToken ct);

    /// <summary>Bytes DAHİL tek görsel — indirme endpoint'i için.</summary>
    Task<PageCommentImage?> GetImageBinaryAsync(string id, CancellationToken ct);

    Task<bool> DeleteImageAsync(string id, CancellationToken ct);

    Task AddActivityAsync(PageCommentActivity activity, CancellationToken ct);

    /// <summary>Son N aktivite, Created DESC.</summary>
    Task<IReadOnlyList<PageCommentActivity>> ListActivityAsync(int take, CancellationToken ct);
}
