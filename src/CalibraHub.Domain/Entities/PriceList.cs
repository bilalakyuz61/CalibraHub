using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Fiyat listesi satiri — her satir TEK bir fiyati tasir (PriceType: 'b'=alis, 's'=satis; ileride 'm'=maliyet eklenebilir). Ayni urun/grup/donem icin alis ve satis ayri satirlardir. ConfigId ile konfigurasyonlu urunler icin varyant bazli fiyatlandirma.")]
public sealed class PriceList
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int ItemId { get; set; }
    public int? ConfigId { get; set; }
    public int CurrencyId { get; set; }
    /// <summary>
    /// Fiyat tipi — tek harf kod: "b" (buying / alis), "s" (selling / satis).
    /// Ileride "m" (maliyet) gibi yeni tipler eklenebilir; NVARCHAR(10) yer var.
    /// </summary>
    public string PriceType { get; set; } = "s";
    public decimal Price { get; set; }
    public DateTime ValidFrom { get; set; } = DateTime.Today;
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
