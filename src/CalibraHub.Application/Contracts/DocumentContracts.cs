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

/// <summary>
/// DocumentLineDto — UI'a gonderilen satir goruntusu. ItemId + CombinationId tablodaki
/// gercek FK'ler; MaterialCode/Name ve CombinationCode/Name Item ve ProductConfiguration
/// tablolarindan JOIN ile turetilir (tabloda tutulmaz).
/// </summary>
public sealed record DocumentLineDto(
    int Id, int DocumentId, int LineNo,
    int ItemId, string? MaterialCode, string? MaterialName,
    int? UnitId, string? UnitCode, string? UnitName,
    decimal Quantity, decimal UnitPrice, decimal DiscountRate, decimal LineTotal,
    int? CombinationId, string? CombinationCode,
    int? LocationId, string? LocationCode, string? LocationName,
    string? Notes,
    IReadOnlyList<DocumentLineDetailDto>? CombinationDetails = null,
    bool NotesPinned = false,
    // Revize zinciri — NULL ise orijinal satir, aksi halde revize edildigi satirin Id'si.
    int? RevisedFromId = null);

public sealed record DocumentLineDetailDto(
    int Id, int QuoteLineId,
    string FeatureName, string ValueCode, string ValueName,
    string? Description, int LineOrder);

public sealed record DocumentListItemDto(
    int Id, string DocumentNumber, DateTime DocumentDate, DateTime? ValidUntil,
    string? ContactName, string Currency, decimal GrandTotal,
    string Status, int RevisionNo, bool IsActive, int LineCount,
    int? ContactId = null,
    int? DocumentTypeId = null);

public sealed record SaveDocumentRequest(
    int? Id, DateTime DocumentDate, DateTime? ValidUntil,
    int? ContactId, string? ContactName, string? ContactAddress,
    int? SalesRepId,
    string Currency, decimal DiscountRate, decimal TaxRate,
    string? PaymentTerms, string? DeliveryTerms, string? DeliveryAddress,
    string? Notes, IReadOnlyCollection<SaveDocumentLineRequest> Lines,
    string? ContactCode = null,
    int? DocumentTypeId = null);

/// <summary>
/// Client'tan gelen kayit istegi. ItemId zorunludur — malzeme kodu/adi tabloda tutulmaz,
/// Item kartindan turetilir. CombinationId opsiyoneldir (kombinasyon takipli stoklarda
/// dolu gelmesi beklenir).
/// </summary>
public sealed record SaveDocumentLineRequest(
    int? Id, int ItemId, int? UnitId,
    decimal Quantity, decimal UnitPrice, decimal DiscountRate,
    int? CombinationId, int? LocationId, string? Notes,
    IReadOnlyList<SaveQuoteLineDetailItem>? CombinationDetails = null,
    /// <summary>
    /// Client tarafindan doldurulur — Item.TrackCombinations. Server-side save sirasinda
    /// true ise CombinationId zorunlu olarak kontrol edilir.
    /// </summary>
    bool TrackCombinations = false,
    bool NotesPinned = false,
    /// <summary>
    /// Revize kaynagi — bu satir hangi mevcut satirdan revize edildi?
    /// NULL ise normal bir yeni satir / duzenleme. Dolu ise yeni satir INSERT'ine
    /// revised_from_id kolonuna yazilir; zincir geriye takip edilebilir.
    /// </summary>
    int? RevisedFromId = null);

public sealed record SaveQuoteLineDetailItem(
    string FeatureName,
    string? FeatureCode,
    string ValueCode,
    string ValueName,
    string? Description,
    int LineOrder);
