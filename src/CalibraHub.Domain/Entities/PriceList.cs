using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Fiyat listesi satirlari — PriceGroup + Item (veya serbest material_code) eslemesiyle alis/satis fiyatlari, para birimi ve gecerlilik tarihleri. CombinationCode ile konfigurasyonlu urunler icin varyant bazli fiyatlandirma.")]
public sealed class PriceList
{
    public int Id { get; set; }
    public int PriceGroupId { get; set; }
    public int? ItemId { get; set; }
    public required string MaterialCode { get; set; }
    public string? MaterialName { get; set; }
    public string? CombinationCode { get; set; }
    public string? CombinationName { get; set; }
    public string Currency { get; set; } = "TRY";
    public decimal BuyingPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public DateTime ValidFrom { get; set; } = DateTime.Today;
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
