namespace CalibraHub.Application.Contracts;

public sealed record SalesQuoteDto(
    Guid Id, string QuoteNumber, DateTime QuoteDate, DateTime? ValidUntil,
    int? CustomerId, string? CustomerName, string? CustomerAddress,
    int? SalesRepId,
    string Currency, decimal SubTotal, decimal DiscountRate, decimal DiscountAmount,
    decimal TaxRate, decimal TaxAmount, decimal GrandTotal,
    string? PaymentTerms, string? DeliveryTerms, string? DeliveryAddress,
    string Status, int RevisionNo, Guid? ParentQuoteId, string? Notes,
    string? CreatedBy, DateTime CreatedAt, DateTime UpdatedAt, bool IsActive);

public sealed record SalesQuoteLineDto(
    Guid Id, Guid QuoteId, int LineNo,
    int? StockCardId, string MaterialCode, string MaterialName, string? UnitName,
    decimal Quantity, decimal UnitPrice, decimal DiscountRate, decimal LineTotal,
    string? CombinationCode, string? Notes, bool IsActive);

public sealed record SalesQuoteListItemDto(
    Guid Id, string QuoteNumber, DateTime QuoteDate, DateTime? ValidUntil,
    string? CustomerName, string Currency, decimal GrandTotal,
    string Status, int RevisionNo, bool IsActive, int LineCount);

public sealed record SaveSalesQuoteRequest(
    Guid? Id, DateTime QuoteDate, DateTime? ValidUntil,
    int? CustomerId, string? CustomerName, string? CustomerAddress,
    int? SalesRepId,
    string Currency, decimal DiscountRate, decimal TaxRate,
    string? PaymentTerms, string? DeliveryTerms, string? DeliveryAddress,
    string? Notes, IReadOnlyCollection<SaveSalesQuoteLineRequest> Lines);

public sealed record SaveSalesQuoteLineRequest(
    Guid? Id, int? StockCardId, string MaterialCode, string MaterialName, string? UnitName,
    decimal Quantity, decimal UnitPrice, decimal DiscountRate, string? CombinationCode, string? Notes);
