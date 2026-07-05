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
/// FulfillmentCenter — İhtiyaç kaydını kapat (Converted statüsüne al).
/// POST /Purchase/CloseRequests
/// </summary>
public sealed record CloseRequestsModel(IReadOnlyList<int> RequestIds);

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
/// Depodan Karşıla — seçili ihtiyaç kalemlerini FIFO stok dağıtımıyla karşıla.
/// POST /Purchase/FulfillFromStock
/// </summary>
public sealed record FulfillFromStockRequest(
    IReadOnlyList<int> LineIds,
    string?            Notes);
