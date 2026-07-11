namespace CalibraHub.Application.Contracts;

public sealed record DocumentDto(
    int Id, string DocumentNumber, DateTime DocumentDate, DateTime? ValidUntil,
    int? ContactId, string? ContactName, string? ContactAddress,
    int? SalesRepId,
    // CurrencyId DB'deki gercek FK; CurrencyCode/Symbol display alanlari (currencies JOIN'den)
    int CurrencyId,
    decimal SubTotal, decimal DiscountRate, decimal DiscountAmount,
    decimal TaxRate, decimal TaxAmount, decimal GrandTotal,
    string? PaymentTerms, string? DeliveryTerms, string? DeliveryAddress,
    string Status, int RevisionNo, int? ParentDocumentId, string? Notes,
    int? CreatedById, DateTime CreatedAt, DateTime UpdatedAt, bool IsActive,
    string? ContactCode = null,
    int? DocumentTypeId = null,
    DateTime? DeliveryDate = null,        // Faz M — sipariş için talep edilen teslim tarihi
    int? DeliveryDays = null,             // Faz M — teslim süresi (gün), DocumentDate'e eklenir
    string? CurrencyCode = null,          // Display (TRY/USD/EUR) — currencies.code
    string? CurrencySymbol = null,        // Display (₺/$/€) — currencies.symbol
    // Talep Eden — Personnel.Id FK (İhtiyaç Kaydı).
    int? RequesterPersonnelId = null,
    string? RequesterPersonnelName = null,   // Personnel.FullName (JOIN ile gelir)
    // Hedef Lokasyon — Location.Id FK (İhtiyaç Kaydı başlık düzeyi).
    int? LocationId = null,
    string? LocationName = null);            // Location.LocationName (JOIN ile gelir)

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
    int? RevisedFromId = null,
    // Kalem bazli kaynak iz — bu satir hangi kaynak satirdan turetildi (siparis clone'u).
    int? SourceLineId = null,
    // İhtiyaç Kaydı karşılama takip alanları (alis_talebi satırları için; diğerlerinde 0).
    decimal FulfilledFromStock = 0m,
    decimal FulfilledByPurchase = 0m,
    int FulfillmentStatus = 0);

public sealed record DocumentLineDetailDto(
    int Id, int QuoteLineId,
    string FeatureName, string ValueCode, string ValueName,
    string? Description, int LineOrder);

public sealed record DocumentListItemDto(
    int Id, string DocumentNumber, DateTime DocumentDate, DateTime? ValidUntil,
    string? ContactName,
    int CurrencyId,
    decimal GrandTotal,
    string Status, int RevisionNo, bool IsActive, int LineCount,
    int? ContactId = null,
    int? DocumentTypeId = null,
    string? CurrencyCode = null,
    string? CurrencySymbol = null,
    // Talep Eden — Personnel.Id FK (İhtiyaç Kaydı liste kartları).
    int? RequesterPersonnelId = null,
    string? RequesterPersonnelName = null,
    // İhtiyaç Kaydı satır karşılama özeti (alis_talebi listesi için; diğerlerinde 0).
    int FulfillPending = 0,
    int FulfillPartial = 0,
    int FulfillFull = 0);

public sealed record SaveDocumentRequest(
    int? Id, DateTime DocumentDate, DateTime? ValidUntil,
    int? ContactId, string? ContactName, string? ContactAddress,
    int? SalesRepId,
    int CurrencyId, decimal DiscountRate, decimal TaxRate,
    string? PaymentTerms, string? DeliveryTerms, string? DeliveryAddress,
    string? Notes, IReadOnlyCollection<SaveDocumentLineRequest> Lines,
    string? ContactCode = null,
    int? DocumentTypeId = null,
    DateTime? DeliveryDate = null,        // Faz M — sipariş için
    int? DeliveryDays = null,             // Faz M — teslim süresi (gün)
    // Talep Eden — Personnel.Id FK (İhtiyaç Kaydı).
    int? RequesterPersonnelId = null,
    // Kaynak İhtiyaç Kaydı ID'si — teklif/sipariş bu İhtiyaç'tan türetiliyorsa set edilir.
    // Yeni belge kaydedilince document_source köprüsü otomatik eklenir.
    int? FromRequestId = null,
    // Hedef Lokasyon — Location.Id FK (İhtiyaç Kaydı başlık düzeyi).
    int? LocationId = null);

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
    int? RevisedFromId = null,
    /// <summary>
    /// Seri-takipli kalemde siparise secilen seri numaralari (pick modu). Server-side
    /// kayit sonrasi DocumentLineSerial'e baglanir; ORDER_SERIAL_RESERVATION + stok rez.
    /// acikken InStock→Reserved yapilir (ReconcileOrderSerialsAsync).
    /// </summary>
    IReadOnlyList<string>? Serials = null);

public sealed record SaveQuoteLineDetailItem(
    string FeatureName,
    string? FeatureCode,
    string ValueCode,
    string ValueName,
    string? Description,
    int LineOrder);

/// <summary>
/// Tekliften siparis olusturma istegi. QuoteIds icindeki teklifler cari bazinda
/// gruplanir ve her cari icin tek bir siparis (Document, type=satis_siparisi) uretilir.
/// Kaynak teklifin durumu Converted'a geciler ve document_source koprusu ile baglanir.
/// </summary>
public sealed record CreateOrdersFromQuotesRequest(
    IReadOnlyCollection<int> QuoteIds,
    DateTime OrderDate);

public sealed record CreateOrdersFromQuotesResult(
    bool Success,
    string? Error,
    int OrdersCreated,
    IReadOnlyList<int> OrderIds);

/// <summary>Tek bir teklifi siparise donusturen request. CreateWorkOrders=true ise
/// olusan siparişin her satiri icin yeni bir is emri (WorkOrder) acilir.</summary>
public sealed record ConvertSingleQuoteToOrderRequest(
    int QuoteId,
    DateTime OrderDate,
    bool CreateWorkOrders);

public sealed record ConvertSingleQuoteToOrderResult(
    bool Success,
    string? Error,
    int? OrderId,
    IReadOnlyList<int>? WorkOrderIds);
