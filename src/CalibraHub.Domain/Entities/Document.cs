using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class Document : Entity
{
    public required string DocumentNumber { get; init; }
    public DateTime DocumentDate { get; set; } = DateTime.Now;
    public DateTime? ValidUntil { get; set; }
    public int? DocumentTypeId { get; set; }
    public int? ContactId { get; set; }
    public string? ContactName { get; set; }
    public string? ContactAddress { get; set; }
    /// <summary>Contact.AccountCode — transient (join ile doldurulur, tabloda saklanmaz).</summary>
    public string? ContactCode { get; set; }
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
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public int RevisionNo { get; set; }
    public Guid? ParentDocumentId { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsActive { get; set; } = true;

    /// <summary>Computed — satır sayısı (liste sorgusu için).</summary>
    public int LineCount { get; set; }
}
