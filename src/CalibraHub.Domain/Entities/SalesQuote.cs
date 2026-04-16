using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class SalesQuote : Entity
{
    public required string QuoteNumber { get; init; }
    public DateTime QuoteDate { get; set; } = DateTime.Now;
    public DateTime? ValidUntil { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerAddress { get; set; }
    public int? SalesRepId { get; set; }
    public string Currency { get; set; } = "TRY";
    public decimal SubTotal { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; } = 20m;
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public string? PaymentTerms { get; set; }
    public string? DeliveryTerms { get; set; }
    public string? DeliveryAddress { get; set; }
    public SalesQuoteStatus Status { get; set; } = SalesQuoteStatus.Draft;
    public int RevisionNo { get; set; }
    public Guid? ParentQuoteId { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsActive { get; set; } = true;

    /// <summary>Computed — satır sayısı (liste sorgusu için).</summary>
    public int LineCount { get; set; }
}
