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
    string? LocationName = null,             // Location.LocationName (JOIN ile gelir)
    // PageComment Seq 1067 — belge başında elle girilen kur. 1 birim belge dövizi = ExchangeRate TL.
    decimal ExchangeRate = 1m,
    // PageComment Seq 1068 — "KDV Dahil" başlık switch'i. true ise satır UnitPrice brüt (KDV dahil) kabul edilir.
    bool IsVatIncluded = false,
    // 2026-08-06 — kurun ALINDIĞI tarih (belge tarihinden farklı olabilir). Yalnız izlenebilirlik
    // ve ekranda doğru gösterim için; tutar hesabı kayıtlı ExchangeRate ile yapılır.
    DateTime? RateDate = null,
    // PageComment Seq 1106 — dış sistemin (ERP) belge numarası. İçe aktarımda mükerrer koruması
    // için tutulur; CalibraHub'ın kendi DocumentNumber serisinin yerine GEÇMEZ.
    string? SourceDocumentNo = null,
    // 2026-08-27 — iyimser eşzamanlılık damgası (ROWVERSION, base64). Ekran belgeyi
    // açarken alır, kaydederken geri gönderir; arada başkası kaydettiyse çakışma döner.
    string? RowVersion = null);

/// <summary>
/// Sipariş → İrsaliye kısmi teslimat modalı için açık (teslim edilmemiş) sipariş kalemi.
/// Miktarlar gösterim biriminde (Quantity birimi): Ordered = sipariş miktarı,
/// Delivered = şimdiye kadar teslim, Open = kalan (teslim edilebilir). SerialTracked=true ise
/// seri-takipli malzeme (teslim adedince seri otomatik düşülür).
/// </summary>
public sealed record OrderOpenLineDto(
    int LineId, string? ItemCode, string? ItemName, string? UnitName,
    decimal Ordered, decimal Delivered, decimal Open, bool SerialTracked,
    // Kit tam-set kısmi teslimat: satır bir KİT (ItemType.Kit) ise IsKit=true; miktar birimi = "set"
    // (yalnız tam set teslim edilebilir). KitComponents = donmuş snapshot'tan 1-set bileşen dökümü.
    bool IsKit = false,
    IReadOnlyList<KitComponentBriefDto>? KitComponents = null,
    // Faz 3 — kit satırının kaynak deposu (kalem deposu yoksa belge deposu). Modal, bileşen
    // seri/lot seçim sorgusunu (GetSerialsJson) bu depoya göre yapar.
    int? SourceLocationId = null);

/// <summary>
/// Kit kısmi teslimat modalı için bileşen özeti: 1 set için gereken oran (PerSet) + bileşenin
/// seri/lot takipli olup olmadığı (takipli ise elle seçim modalı açılır — bkz. Faz 3).
/// </summary>
public sealed record KitComponentBriefDto(
    string? Code, string? Name, decimal PerSet, bool SerialOrLotTracked,
    // Faz 3 — ID-tabanlı eşleşme için bileşenin ItemId/ConfigId'si (modal seçimini
    // KitComponentPickDto ile geri gönderirken kullanılır) + takip tipi (None/Serial/Lot).
    int ComponentItemId = 0, int? ConfigId = null, string Tracking = "None");

/// <summary>
/// Faz 3 — kit kısmi teslimat modalından bileşen bazında elle seçilen seri/lot. ComponentItemId +
/// ConfigId, DocumentLineKitComponent snapshot'ındaki bileşenle ID-tabanlı eşleşir (CLAUDE.md ID
/// eşleştirme kuralı). Serials seri-takipli bileşen için (adet = teslim edilen set × PerKit),
/// LotCode lot-takipli bileşen için mevcut lot numarasıdır.
/// </summary>
public sealed record KitComponentPickDto(
    int ComponentItemId, int? ConfigId, IReadOnlyList<string>? Serials, string? LotCode);

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
    int FulfillmentStatus = 0,
    // Faz 4a — kit tam-set teslimat patlaması: bu satır hangi kit BAŞLIK satırından türedi
    // (aynı belgede). NULL = kit bileşeni değil. Grid, kit başlık+bileşen satırlarını bunun
    // üzerinden görsel gruplar (başlık rozet, bileşen girinti).
    int? KitParentLineId = null,
    // Bu satırın malzemesi kit/paket ürün mü (Items.TypeId = ItemType.Kit = 10)? Grid, kit
    // satırının bileşenlerini KitLineComponents(lineId) ile açılır gösterir. Salt-okuma
    // display alanı — tabloda tutulmaz, Items JOIN'inden türetilir (MaterialCode/Name gibi).
    bool IsKit = false);

public sealed record DocumentLineDetailDto(
    int Id, int QuoteLineId,
    string FeatureName, string ValueCode, string ValueName,
    string? Description, int LineOrder);

/// <summary>
/// Bir belge satırının "bağlantı kilidi" — düzenleme ekranı bu bilgiyle satırı önden
/// korur: kilitli satır silinemez ve miktarı <see cref="MinQuantity"/> altına düşürülemez.
/// Kilit nedeni: karşılanmış miktar (İhtiyaç Kaydı) veya bu satırdan türetilmiş AKTİF belge
/// satırları. Kilitli olmayan satırlar listeye eklenmez. Sunucu tarafı zorunluluğu yine
/// SaveQuoteAsync'te (aynı taban) uygulanır — bu yalnızca UI'ın önden uyarması içindir.
/// </summary>
public sealed record DocumentLineLockDto(int LineId, decimal MinQuantity, string Reason);

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
    int? LocationId = null,
    // PageComment Seq 1067 — belge başında elle girilen kur. 1 birim belge dövizi = ExchangeRate TL.
    decimal ExchangeRate = 1m,
    // PageComment Seq 1068 — "KDV Dahil" başlık switch'i. true ise satır UnitPrice brüt (KDV dahil) kabul edilir.
    bool IsVatIncluded = false,
    // 2026-08-06 — kurun ALINDIĞI tarih (belge tarihinden farklı olabilir). Yalnız izlenebilirlik
    // ve ekranda doğru gösterim için; tutar hesabı kayıtlı ExchangeRate ile yapılır.
    DateTime? RateDate = null,
    // PageComment Seq 1106 — dış sistemin (ERP) belge numarası. YALNIZ içe aktarım akışı doldurur
    // (kullanıcı ekranından gelmez); mükerrer belge açılmaması için kaynak eşlemesi olarak
    // saklanır. NULL bırakılırsa mevcut davranış birebir korunur.
    string? SourceDocumentNo = null,
    // 2026-08-27 — belge açıldığında okunan ROWVERSION damgası (base64). Gönderilirse
    // sunucu UPDATE'i bu damgayla şartlar: arada başkası kaydettiyse istek reddedilir.
    // NULL bırakılırsa kontrol atlanır (mobil/entegrasyon çağıranları kırılmaz).
    string? RowVersion = null);

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
