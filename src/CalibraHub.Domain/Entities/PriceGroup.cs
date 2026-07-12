using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Fiyat gruplari (bayilik/perakende/VIP/kampanya vb.). Contact.PriceGroupId ile cariler bu gruba baglanir, PriceList satirlari da bu grupla eslesir.")]
public sealed class PriceGroup
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // Bu gruba hangi fiyat tiplerinde kayit girilebilir? (en az 1 tane true olmali)
    // Default true: yeni gruplar tum tipleri kabul eder; kullanici kisitlamak isterse kapatir.
    public bool AllowsBuying  { get; set; } = true;
    public bool AllowsSelling { get; set; } = true;
    public bool AllowsCost    { get; set; } = true;

    // "Genel Liste" isareti: cariye ozel liste yoksa VEYA cari listesinde urun fiyati
    // yoksa fiyat buradan cozulur (fallback). CompanyId basina TEK default olur
    // (UX_PriceGroup_Company_Default filtered unique index garanti eder). Bu grup silinemez.
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
