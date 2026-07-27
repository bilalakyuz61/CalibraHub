using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Doküman kategori/tip iki seviyeli ağaç (master DB). <see cref="ParentId"/> NULL ise
/// Kategori (L1), dolu ise Tip (L2, self-FK). Daha derin iç içelik desteklenmez — bir Tip'in
/// altına Tip eklenemez (bkz. DocumentCategoryService iş kuralı).
/// Attachment.DocumentCategoryId seçilen en spesifik düğüme (Tip varsa Tip, yoksa Kategori) işaret eder.
/// Kullanıcı kod girmez kuralı: bu tabloda Code kolonu yok, uniqueness Name üzerinden (parent-scope).
/// </summary>
[Description("Doküman kategori/tip iki seviyeli ağaç. ParentId NULL=Kategori(L1), dolu=Tip(L2, self-FK).")]
public sealed class DocumentCategory
{
    public int Id { get; set; }

    [Description("Üst düğüm. NULL ise bu bir Kategori (L1); dolu ise bu bir Tip (L2), FK -> DocumentCategory.Id.")]
    public int? ParentId { get; set; }

    [Description("Kategori/Tip adı — kullanıcıya gösterim. Parent-scope unique (aynı ParentId altında tekrar edemez).")]
    public required string Name { get; set; }

    public int SortOrder { get; set; }

    [Description("Soft delete — pasif düğümler listede/dropdown'da gösterilmez.")]
    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
