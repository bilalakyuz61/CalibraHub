namespace CalibraHub.Domain.Entities;

/// <summary>
/// FldSet — Form sabit alanlarinin rehber eslestirmesi ve ayarlari.
///
/// Her satir bir formun (dbo.Forms) belirli bir HTML input alanini tanimlar.
/// ViewName veya GuideCode dolu ise o alana rehber lookup davranisi uygulanir
/// (readonly + arama). ViewName onceliklidir; geriye uyumluluk icin GuideCode
/// hala okunup yaziliyor (PR 3'te kaldirilacak).
/// FilterJson, rehber arama sirasinda uygulanacak cascading constraint JSON'unu tutar.
/// FormatJson schema'si: { visibleColumns: [], columnLabels: {}, valueColumn, displayColumn, sortColumn?, distinctTextual? }
///
/// Properties ADO.NET reader ile uyumlu olsun diye set'li (init degil).
/// </summary>
public sealed class FieldSetting
{
    public int Id { get; set; }
    public int FormId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string FieldLabel { get; set; } = string.Empty;
    /// <summary>
    /// LEGACY — PR 3'te kaldirilacak. ViewName kullanin. Co-existence donemi
    /// boyunca repo iki kolonu da yazip okur.
    /// </summary>
    public string? GuideCode { get; set; }
    /// <summary>
    /// PRIMARY (PR 1+) — Sabit alanin direkt baglandigi SQL view adi
    /// (orn. `cbv_Guide_Items`). GuideMas indirection'i kalmadi; tum
    /// metadata (value/display column, gorunur kolonlar, vb.) FormatJson'da.
    /// </summary>
    public string? ViewName { get; set; }
    public string? FilterJson { get; set; }
    public bool IsRequired { get; set; }
    public string? FormatJson { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
