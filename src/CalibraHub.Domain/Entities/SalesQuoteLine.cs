using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class SalesQuoteLine : Entity
{
    public Guid QuoteId { get; init; }
    public int LineNo { get; set; }
    public int? StockCardId { get; set; }
    public required string MaterialCode { get; set; }
    public required string MaterialName { get; set; }
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal LineTotal { get; set; }
    public string? CombinationCode { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}
