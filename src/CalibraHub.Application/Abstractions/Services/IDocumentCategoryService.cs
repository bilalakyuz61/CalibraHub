using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>Doküman kategori/tip (iki seviyeli ağaç) CRUD servisi — PageComment Seq 47.</summary>
public interface IDocumentCategoryService
{
    /// <summary>Tüm aktif düğümler (Kategori + Tip) — yönetim ekranı ağaç görünümü için.</summary>
    Task<IReadOnlyCollection<DocumentCategoryDto>> GetAllAsync(CancellationToken ct);

    /// <summary>Yalnız Kategori (L1) — üst dropdown.</summary>
    Task<IReadOnlyCollection<DocumentCategoryDto>> GetLevel1Async(CancellationToken ct);

    /// <summary>Seçilen Kategori'nin Tip (L2) çocukları — alt dropdown.</summary>
    Task<IReadOnlyCollection<DocumentCategoryDto>> GetByParentAsync(int parentId, CancellationToken ct);

    Task<DocumentCategoryDto?> GetByIdAsync(int id, CancellationToken ct);

    Task<(bool Success, string? Error, int? Id)> CreateAsync(CreateDocumentCategoryRequest request, int? actorUserId, CancellationToken ct);

    Task<(bool Success, string? Error)> UpdateAsync(UpdateDocumentCategoryRequest request, int? actorUserId, CancellationToken ct);

    /// <summary>Soft delete — Kategori siliniyorsa alt Tip'leri de pasifleşir.</summary>
    Task<(bool Success, string? Error)> DeleteAsync(int id, int? actorUserId, CancellationToken ct);
}
