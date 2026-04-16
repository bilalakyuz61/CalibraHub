namespace CalibraHub.Domain.Entities;

public sealed class PriceListEntry
{
    public int Id { get; set; }
    public int PriceGroupId { get; set; }
    public int? StockCardId { get; set; }
    public required string MaterialCode { get; set; }
    public string? MaterialName { get; set; }
    public string Currency { get; set; } = "TRY";
    public decimal BuyingPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public DateTime ValidFrom { get; set; } = DateTime.Today;
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
