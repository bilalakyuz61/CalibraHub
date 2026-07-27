using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Calibra master DB'deki DocumentCategory tablosu için repository arayüzü — Attachment ile
/// aynı bağlantıyı kullanır (OpenSystemConnectionAsync, per-company routing'den bağımsız).
/// Salt CRUD; uniqueness/derinlik validasyonu DocumentCategoryService katmanındadır.
/// </summary>
public interface IDocumentCategoryRepository
{
    /// <summary>Tüm aktif düğümler (Kategori + Tip), SortOrder sonra Name sıralı.</summary>
    Task<IReadOnlyCollection<DocumentCategory>> ListAllAsync(CancellationToken ct);

    /// <summary>Yalnız Kategori (L1) düğümleri — ParentId IS NULL.</summary>
    Task<IReadOnlyCollection<DocumentCategory>> ListLevel1Async(CancellationToken ct);

    /// <summary>Belirli bir Kategori'nin Tip (L2) çocukları.</summary>
    Task<IReadOnlyCollection<DocumentCategory>> ListByParentAsync(int parentId, CancellationToken ct);

    Task<DocumentCategory?> GetByIdAsync(int id, CancellationToken ct);

    Task<int> AddAsync(DocumentCategory category, CancellationToken ct);

    Task UpdateAsync(DocumentCategory category, CancellationToken ct);

    /// <summary>IsActive=0 yapar. id bir Kategori ise (ParentId NULL) altındaki tüm Tip'ler de
    /// aynı işlemde pasifleştirilir (tek SQL, kaskad).</summary>
    Task SoftDeleteAsync(int id, int? updatedById, CancellationToken ct);
}
