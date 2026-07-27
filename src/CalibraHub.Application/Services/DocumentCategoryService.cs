using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

/// <summary>
/// Doküman kategori/tip (iki seviyeli ağaç) servisi — kullanıcı kod girmez kuralı (tabloda
/// zaten Code kolonu yok); uniqueness Name üzerinden, parent-scope (aynı ParentId altında).
/// Derinlik kısıtı: bir Tip'in (ParentId dolu düğüm) altına yeni düğüm eklenemez — yalnız
/// Kategori (ParentId NULL) çocuk alabilir.
/// </summary>
public sealed class DocumentCategoryService : IDocumentCategoryService
{
    private readonly IDocumentCategoryRepository _repo;
    private readonly IAuditTrailService? _audit;

    public DocumentCategoryService(IDocumentCategoryRepository repo, IAuditTrailService? audit = null)
    {
        _repo = repo;
        _audit = audit;
    }

    public async Task<IReadOnlyCollection<DocumentCategoryDto>> GetAllAsync(CancellationToken ct)
    {
        var all = await _repo.ListAllAsync(ct);
        return all.Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyCollection<DocumentCategoryDto>> GetLevel1Async(CancellationToken ct)
    {
        var list = await _repo.ListLevel1Async(ct);
        return list.Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyCollection<DocumentCategoryDto>> GetByParentAsync(int parentId, CancellationToken ct)
    {
        var list = await _repo.ListByParentAsync(parentId, ct);
        return list.Select(ToDto).ToArray();
    }

    public async Task<DocumentCategoryDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var e = await _repo.GetByIdAsync(id, ct);
        return e is null ? null : ToDto(e);
    }

    public async Task<(bool Success, string? Error, int? Id)> CreateAsync(
        CreateDocumentCategoryRequest request, int? actorUserId, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
            return (false, "Ad boş olamaz.", null);

        if (request.ParentId.HasValue)
        {
            var parent = await _repo.GetByIdAsync(request.ParentId.Value, ct);
            if (parent is null || !parent.IsActive)
                return (false, "Seçilen kategori bulunamadı.", null);
            if (parent.ParentId.HasValue)
                return (false, "Bir Tipin altına Tip eklenemez — Tip yalnızca bir Kategori altında olabilir.", null);
        }

        var siblings = request.ParentId.HasValue
            ? await _repo.ListByParentAsync(request.ParentId.Value, ct)
            : await _repo.ListLevel1Async(ct);

        if (siblings.Any(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            return (false, $"Aynı isimde başka bir kayıt zaten tanımlı: '{name}'", null);

        var entity = new DocumentCategory
        {
            ParentId    = request.ParentId,
            Name        = name,
            SortOrder   = request.SortOrder,
            IsActive    = request.IsActive,
            CreatedById = actorUserId,
            Created     = DateTime.UtcNow,
        };
        var newId = await _repo.AddAsync(entity, ct);

        _audit?.LogInsert(
            request.ParentId.HasValue ? "DocumentCategoryType" : "DocumentCategory",
            newId, name, snapshot: entity);

        return (true, null, newId);
    }

    public async Task<(bool Success, string? Error)> UpdateAsync(
        UpdateDocumentCategoryRequest request, int? actorUserId, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
            return (false, "Ad boş olamaz.");

        var existing = await _repo.GetByIdAsync(request.Id, ct);
        if (existing is null)
            return (false, "Kayıt bulunamadı.");

        if (request.ParentId == request.Id)
            return (false, "Bir kayıt kendi üst kategorisi olamaz.");

        if (request.ParentId.HasValue)
        {
            var parent = await _repo.GetByIdAsync(request.ParentId.Value, ct);
            if (parent is null || !parent.IsActive)
                return (false, "Seçilen kategori bulunamadı.");
            if (parent.ParentId.HasValue)
                return (false, "Bir Tipin altına Tip eklenemez — Tip yalnızca bir Kategori altında olabilir.");
        }

        // Kategori (L1) → Tip (L2) dönüşümü, altında çocuğu varsa 3 seviyeli hiyerarşi
        // üretir — engellenir (ParentId NULL idi, şimdi doluya çevriliyorsa kontrol et).
        if (!existing.ParentId.HasValue && request.ParentId.HasValue)
        {
            var children = await _repo.ListByParentAsync(request.Id, ct);
            if (children.Count > 0)
                return (false, "Alt tipi olan bir Kategori, Tip'e dönüştürülemez.");
        }

        var siblings = request.ParentId.HasValue
            ? await _repo.ListByParentAsync(request.ParentId.Value, ct)
            : await _repo.ListLevel1Async(ct);

        if (siblings.Any(x => x.Id != request.Id &&
                               string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            return (false, $"Aynı isimde başka bir kayıt zaten tanımlı: '{name}'");

        // İşlem logu: entity mutasyondan ÖNCE eski değerler snapshot'lanır.
        var auditOld = new { existing.ParentId, existing.Name, existing.SortOrder, existing.IsActive };

        existing.ParentId  = request.ParentId;
        existing.Name      = name;
        existing.SortOrder = request.SortOrder;
        existing.IsActive  = request.IsActive;
        existing.UpdatedById = actorUserId;
        existing.Updated   = DateTime.UtcNow;

        await _repo.UpdateAsync(existing, ct);

        _audit?.LogUpdate(
            existing.ParentId.HasValue ? "DocumentCategoryType" : "DocumentCategory",
            existing.Id, name, auditOld,
            new { existing.ParentId, existing.Name, existing.SortOrder, existing.IsActive });

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int id, int? actorUserId, CancellationToken ct)
    {
        var existing = await _repo.GetByIdAsync(id, ct);
        if (existing is null)
            return (false, "Kayıt bulunamadı.");

        await _repo.SoftDeleteAsync(id, actorUserId, ct);

        _audit?.LogDelete(
            existing.ParentId.HasValue ? "DocumentCategoryType" : "DocumentCategory",
            id, existing.Name);

        return (true, null);
    }

    private static DocumentCategoryDto ToDto(DocumentCategory e) =>
        new(e.Id, e.ParentId, e.Name, e.SortOrder, e.IsActive);
}
