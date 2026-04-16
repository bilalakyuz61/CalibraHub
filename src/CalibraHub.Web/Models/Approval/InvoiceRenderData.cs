namespace CalibraHub.Web.Models.Approval;

public sealed class InvoiceRenderData
{
    public string? ProfileId { get; init; }
    public string? TypeCode { get; init; }
    public string? Currency { get; init; }
    public string? Note { get; init; }
    public InvoiceParty? Supplier { get; init; }
    public InvoiceParty? Customer { get; init; }
    public IReadOnlyList<InvoiceLineItem> Lines { get; init; } = Array.Empty<InvoiceLineItem>();
    public IReadOnlyList<InvoiceTaxSummary> TaxSummaries { get; init; } = Array.Empty<InvoiceTaxSummary>();
    public string? LineExtensionAmount { get; init; }
    public string? TaxAmount { get; init; }
    public string? PayableAmount { get; init; }
}

public sealed class InvoiceParty
{
    public string? Name { get; init; }
    public string? TaxNumber { get; init; }
    public string? TaxOfficeName { get; init; }
    public string? AddressLine { get; init; }
    public string? City { get; init; }
    public string? Country { get; init; }
}

public sealed class InvoiceLineItem
{
    public string? LineNo { get; init; }
    public string? ItemName { get; init; }
    public string? Quantity { get; init; }
    public string? UnitCode { get; init; }
    public string? UnitPrice { get; init; }
    public string? LineAmount { get; init; }
    public string? TaxRate { get; init; }
    public string? TaxAmount { get; init; }
}

public sealed class InvoiceTaxSummary
{
    public string? TaxName { get; init; }
    public string? Rate { get; init; }
    public string? TaxableAmount { get; init; }
    public string? TaxAmount { get; init; }
}
