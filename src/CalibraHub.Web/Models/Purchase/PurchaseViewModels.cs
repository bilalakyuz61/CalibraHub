namespace CalibraHub.Web.Models.Purchase;

/// <summary>
/// FulfillmentCenter — Depo transferi oluşturma isteği.
/// POST /Purchase/CreateTransfer
/// requestIds: kaynak İhtiyaç Kaydı Document.Id listesi (çoklu seçim).
/// </summary>
public sealed record CreateTransferRequest(
    IReadOnlyList<int> RequestIds,
    string?            Notes,
    IReadOnlyList<TransferLineRequest> Lines);

public sealed record TransferLineRequest(
    int     ItemId,
    int?    UnitId,
    decimal Qty,
    int     FromLocationId,
    int?    ToLocationId,
    int?    CombinationId,
    string? Notes,
    // Karşılanan ihtiyaç satırı — fulfillment takibi için (null ise tracking yok)
    int?    RequestLineId = null);

/// <summary>
/// FulfillmentCenter — Ambar çıkış fişi oluşturma isteği (STOCK_OUT).
/// POST /Purchase/CreateStockIssue
/// </summary>
public sealed record CreateStockIssueRequest(
    IReadOnlyList<int> RequestIds,
    string?            Notes,
    IReadOnlyList<StockIssueLineRequest> Lines);

public sealed record StockIssueLineRequest(
    int     ItemId,
    int?    UnitId,
    decimal Qty,
    int     FromLocationId,
    int?    CombinationId,
    string? Notes,
    int?    RequestLineId = null);

/// <summary>
/// FulfillmentCenter — Satın alma talebi oluşturma isteği (alis_siparisi).
/// POST /Purchase/CreatePurchaseOrderFromIhtiyac
/// Tedarikçi zorunlu değil — belge sonradan düzenlenebilir.
/// </summary>
public sealed record CreatePurchaseOrderFromIhtiyacRequest(
    IReadOnlyList<int> RequestIds,
    string?            Notes,
    int?               ContactId,
    IReadOnlyList<PurchaseOrderLineRequest> Lines);

public sealed record PurchaseOrderLineRequest(
    int     ItemId,
    int?    UnitId,
    decimal Qty,
    int?    CombinationId,
    string? Notes,
    int?    RequestLineId = null);

/// <summary>
/// FulfillmentCenter — seçili İhtiyaç KALEMLERİNİN kalan miktarını kapat.
/// POST /Purchase/CloseFulfillmentLines
/// Yalnız verilen satırlar etkilenir (FulfillmentStatus = 3), aynı belgedeki diğer
/// kalemlere dokunulmaz. (Belge bazlı eski CloseRequests ucu 2026-07-22'de kaldırıldı.)
/// </summary>
public sealed record CloseFulfillmentLinesRequest(IReadOnlyList<int> LineIds);

/// <summary>
/// Satın Alma Talebi — seçilen İhtiyaç kalemlerinden belge oluşturma.
/// POST /Purchase/CreatePurchaseDemand
/// İki çağrı şekli desteklenir:
///   - Sihirbaz: yalnızca LineIds → her kalem kalan miktar (Quantity − FulfilledFromStock
///     − FulfilledByPurchase) kadar talep edilir.
///   - FulfillmentCenter: Lines (kalem başına miktar/not) → verilen miktarlar kullanılır.
/// </summary>
public sealed record CreatePurchaseDemandRequest(
    IReadOnlyList<int>? LineIds,
    string?             Notes,
    IReadOnlyList<PurchaseDemandLineInput>? Lines = null);

public sealed record PurchaseDemandLineInput(
    int     LineId,
    decimal Qty,
    string? Notes = null);

/// <summary>
/// Depodan Karşıla (2026-07-21 revizyonu) — seçili ihtiyaç kalemlerini, karşılama deposu
/// (parametre: FULFILLMENT_LOCATION_MODE/IDS) ile ihtiyaç kaydı deposu AYNIYSA Ambar Çıkış
/// Fişi (depo_cikis) oluşturarak karşılar. Stok bakiyesi bu eşleşmede KULLANILMAZ — yalnız
/// depo Id/Code eşleşmesi (bkz. PurchaseController.FulfillFromStock XML doc'u).
/// POST /Purchase/FulfillFromStock
/// Qty = kullanıcının FulfillmentCenter ortak modalında satır bazlı düzenlediği miktar
/// (eski FIFO'nun aksine sunucu otomatik dağıtım yapmaz).
/// OverrideLocationId (2026-07-25, PageComment Seq 26 — "Ambar Çıkış Fişi" ayrı aksiyonu bu
/// endpoint'le birleştirildi): boş/null ise yukarıdaki otomatik depo-eşleşme davranışı AYNEN
/// çalışır. Dolu ise TÜM seçili kalemler bu depodan karşılanır ve depo-eşleşme reddi bu çağrı
/// için uygulanmaz — kaldırılan CreateStockIssue'nun (kullanıcının serbestçe kaynak depo
/// seçtiği eski "Ambar Çıkış Fişi" akışı) birebir karşılığı.
/// </summary>
public sealed record FulfillFromStockRequest(
    IReadOnlyList<FulfillFromStockLineRequest> Lines,
    string?            Notes,
    int?               OverrideLocationId = null);

public sealed record FulfillFromStockLineRequest(
    int     LineId,
    decimal Qty);

/// <summary>
/// "Karşılama Deposu Seçimi" (Admin → Parametreler → İhtiyaç Kayıtları) kaydı.
/// POST /Purchase/SaveFulfillmentLocationConfig — PageComment Seq 34 (2026-07-25).
/// Mode: "SPECIFIC" (belirli depolar) | "ITEM_DEFAULT" (stok kartındaki varsayılan depo).
/// LocationIds: SPECIFIC modda kullanılacak depo Id listesi — sunucu tarafında raf/hücre
/// tipine karşı doğrulanır (bkz. PurchaseController.SaveFulfillmentLocationConfig XML doc'u).
/// </summary>
public sealed record SaveFulfillmentLocationConfigRequest(
    string?    Mode,
    List<int>? LocationIds);
