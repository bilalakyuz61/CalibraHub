namespace CalibraHub.Application.Contracts;

public sealed record DocumentDto(
    Guid Id, string DocumentNumber, DateTime DocumentDate, DateTime? ValidUntil,
    int? ContactId, string? ContactName, string? ContactAddress,
    int? SalesRepId,
    string Currency, decimal SubTotal, decimal DiscountRate, decimal DiscountAmount,
    decimal TaxRate, decimal TaxAmount, decimal GrandTotal,
    string? PaymentTerms, string? DeliveryTerms, string? DeliveryAddress,
    string Status, int RevisionNo, Guid? ParentDocumentId, string? Notes,
    string? CreatedBy, DateTime CreatedAt, DateTime UpdatedAt, bool IsActive,
    string? ContactCode = null);

public sealed record DocumentLineDto(
    Guid Id, Guid DocumentId, int LineNo,
    int? ItemId, string MaterialCode, string MaterialName, string? UnitName,
    decimal Quantity, decimal UnitPrice, decimal DiscountRate, decimal LineTotal,
    string? CombinationCode, string? Notes, bool IsActive,
    IReadOnlyList<DocumentLineDetailDto>? CombinationDetails = null);

public sealed record DocumentLineDetailDto(
    int Id, Guid QuoteLineId,
    string FeatureName, string ValueCode, string ValueName,
    string? Description, int LineOrder);

public sealed record DocumentListItemDto(
    Guid Id, string DocumentNumber, DateTime DocumentDate, DateTime? ValidUntil,
    string? ContactName, string Currency, decimal GrandTotal,
    string Status, int RevisionNo, bool IsActive, int LineCount);

public sealed record SaveDocumentRequest(
    Guid? Id, DateTime DocumentDate, DateTime? ValidUntil,
    int? ContactId, string? ContactName, string? ContactAddress,
    int? SalesRepId,
    string Currency, decimal DiscountRate, decimal TaxRate,
    string? PaymentTerms, string? DeliveryTerms, string? DeliveryAddress,
    string? Notes, IReadOnlyCollection<SaveDocumentLineRequest> Lines,
    string? ContactCode = null);

public sealed record SaveDocumentLineRequest(
    Guid? Id, int? ItemId, string MaterialCode, string MaterialName, string? UnitName,
    decimal Quantity, decimal UnitPrice, decimal DiscountRate, string? CombinationCode, string? Notes,
    IReadOnlyList<SaveQuoteLineDetailItem>? CombinationDetails = null);

public sealed record SaveQuoteLineDetailItem(
    string FeatureName,
    string? FeatureCode,
    string ValueCode,
    string ValueName,
    string? Description,
    int LineOrder);
