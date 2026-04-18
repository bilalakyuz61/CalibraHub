namespace CalibraHub.Application.Contracts;

public sealed record DocumentDto(
    int Id, string DocumentNumber, DateTime DocumentDate, DateTime? ValidUntil,
    int? ContactId, string? ContactName, string? ContactAddress,
    int? SalesRepId,
    string Currency, decimal SubTotal, decimal DiscountRate, decimal DiscountAmount,
    decimal TaxRate, decimal TaxAmount, decimal GrandTotal,
    string? PaymentTerms, string? DeliveryTerms, string? DeliveryAddress,
    string Status, int RevisionNo, int? ParentDocumentId, string? Notes,
    string? CreatedBy, DateTime CreatedAt, DateTime UpdatedAt, bool IsActive,
    string? ContactCode = null,
    int? DocumentTypeId = null);

public sealed record DocumentLineDto(
    int Id, int DocumentId, int LineNo,
    int? ItemId, string MaterialCode, string MaterialName, string? UnitName,
    decimal Quantity, decimal UnitPrice, decimal DiscountRate, decimal LineTotal,
    string? CombinationCode, string? Notes, bool IsActive,
    IReadOnlyList<DocumentLineDetailDto>? CombinationDetails = null);

public sealed record DocumentLineDetailDto(
    int Id, int QuoteLineId,
    string FeatureName, string ValueCode, string ValueName,
    string? Description, int LineOrder);

public sealed record DocumentListItemDto(
    int Id, string DocumentNumber, DateTime DocumentDate, DateTime? ValidUntil,
    string? ContactName, string Currency, decimal GrandTotal,
    string Status, int RevisionNo, bool IsActive, int LineCount);

public sealed record SaveDocumentRequest(
    int? Id, DateTime DocumentDate, DateTime? ValidUntil,
    int? ContactId, string? ContactName, string? ContactAddress,
    int? SalesRepId,
    string Currency, decimal DiscountRate, decimal TaxRate,
    string? PaymentTerms, string? DeliveryTerms, string? DeliveryAddress,
    string? Notes, IReadOnlyCollection<SaveDocumentLineRequest> Lines,
    string? ContactCode = null,
    int? DocumentTypeId = null);

public sealed record SaveDocumentLineRequest(
    int? Id, int? ItemId, string MaterialCode, string MaterialName, string? UnitName,
    decimal Quantity, decimal UnitPrice, decimal DiscountRate, string? CombinationCode, string? Notes,
    IReadOnlyList<SaveQuoteLineDetailItem>? CombinationDetails = null,
    /// <summary>
    /// Client tarafindan doldurulur — Item.TrackCombinations. Server-side save sirasinda
    /// true ise CombinationCode zorunlu olarak kontrol edilir.
    /// </summary>
    bool TrackCombinations = false);

public sealed record SaveQuoteLineDetailItem(
    string FeatureName,
    string? FeatureCode,
    string ValueCode,
    string ValueName,
    string? Description,
    int LineOrder);
