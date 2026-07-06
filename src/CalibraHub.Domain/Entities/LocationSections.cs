using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Lokasyon Tanımlamaları grubu: LocationSection (Bölüm) → LocationSubSection (Alt Bölüm).
/// Genel Tanımlamalar sayfasındaki ikinci grup — Adres Tanımlamaları ile aynı pattern.
/// NOT: Depo raf ağacı (Location tablosu) ayrı sistemdir; bunlar genel amaçlı
/// bölüm/alt bölüm tanımlarıdır. Kod alanları Name'den otomatik türetilir.
/// </summary>
[Description("Bölüm tanımı — lokasyon tanımlamaları grubunun üst seviyesi; ad global benzersiz.")]
public sealed class LocationSection
{
    public int Id { get; init; }
    public string? Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

[Description("Alt Bölüm tanımı — SectionId'ye bağlı; ad bölüm içinde benzersiz.")]
public sealed class LocationSubSection
{
    public int Id { get; init; }
    public int SectionId { get; set; }
    public string? Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
