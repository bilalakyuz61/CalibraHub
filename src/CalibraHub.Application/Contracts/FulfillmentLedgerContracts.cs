namespace CalibraHub.Application.Contracts;

/// <summary>
/// Bir İhtiyaç Kaydı satırının hangi tür belgeyle karşılandığı.
/// DocumentLineFulfillment.FulfillmentType (TINYINT) kolonunda saklanır → mevcut sayısal
/// değerler DEĞİŞTİRİLEMEZ, yalnızca yeni değer eklenebilir.
///
/// KRİTİK — RefDocId iki AYRI tablodan gelir:
///   • Stok tarafı  (Transfer, StockIssue)               → RefDocId = StockDoc.Id
///   • Satın alma tarafı (Quote, Order, Demand)          → RefDocId = Document.Id
/// İki tabloda aynı Id değeri BAŞKA belgelere aittir (StockDoc #3 ≠ Document #3). Bu yüzden
/// deftere yapılan her sorgu — özellikle ters çevirme — HER ZAMAN tip ailesiyle birlikte
/// filtrelenir. Tip filtresi unutulursa bir belgeyi silmek başka bir belgenin karşılamasını
/// geri alır.
/// </summary>
public enum FulfillmentSourceKind : byte
{
    /// <summary>Depolar arası transfer fişi (StockDoc) — FulfilledFromStock kovası.</summary>
    Transfer = 1,

    /// <summary>Satın alma teklifi (Document) — FulfilledByPurchase kovası.</summary>
    PurchaseQuote = 2,

    /// <summary>Satın alma siparişi (Document) — FulfilledByPurchase kovası.</summary>
    PurchaseOrder = 3,

    /// <summary>Ambar çıkış fişi (StockDoc) — FulfilledFromStock kovası.</summary>
    StockIssue = 4,

    /// <summary>Satın alma talebi (Document) — FulfilledByPurchase kovası.</summary>
    PurchaseDemand = 5,
}

/// <summary>
/// <see cref="FulfillmentSourceKind"/> aileleri. Ters çevirme ve toplam hesaplama bu
/// gruplamayı kullanır — tek doğruluk kaynağı burasıdır, SQL'e sayı gömülmez.
/// </summary>
public static class FulfillmentSourceKinds
{
    /// <summary>RefDocId → StockDoc.Id; toplamı FulfilledFromStock kovasına yazılır.</summary>
    public static readonly IReadOnlyList<byte> StockSide = new byte[]
    {
        (byte)FulfillmentSourceKind.Transfer,
        (byte)FulfillmentSourceKind.StockIssue,
    };

    /// <summary>RefDocId → Document.Id; toplamı FulfilledByPurchase kovasına yazılır.</summary>
    public static readonly IReadOnlyList<byte> PurchaseSide = new byte[]
    {
        (byte)FulfillmentSourceKind.PurchaseQuote,
        (byte)FulfillmentSourceKind.PurchaseOrder,
        (byte)FulfillmentSourceKind.PurchaseDemand,
    };

    /// <summary>Bu tür stok tarafı mı (RefDocId bir StockDoc.Id mi)?</summary>
    public static bool IsStockSide(FulfillmentSourceKind kind) =>
        kind is FulfillmentSourceKind.Transfer or FulfillmentSourceKind.StockIssue;

    /// <summary>Silinen belgenin tarafına göre ters çevrilecek tür listesi.</summary>
    public static IReadOnlyList<byte> For(bool stockDocument) => stockDocument ? StockSide : PurchaseSide;
}

/// <summary>
/// Karşılama defterine yazılacak tek kayıt: "şu ihtiyaç satırının şu kadarı, şu belge
/// tarafından karşılandı".
/// </summary>
/// <param name="RequestLineId">Karşılanan İhtiyaç Kaydı satırı (DocumentLine.Id).</param>
/// <param name="Kind">Karşılama türü — <paramref name="RefDocId"/>'nin hangi tabloya ait olduğunu da belirler.</param>
/// <param name="RefDocId">Karşılayan belgenin Id'si (StockDoc.Id veya Document.Id — bkz. <paramref name="Kind"/>).</param>
/// <param name="Quantity">Bu belgenin bu satır için karşıladığı miktar.</param>
/// <param name="RefDocLineId">
/// Karşılayan SATIRIN Id'si — biliniyorsa. Belge kaydedildiği anda satır Id'leri çoğunlukla
/// elde olmadığı için null geçilebilir; ters çevirme <paramref name="RefDocId"/> üzerinden
/// yapıldığından bu alan yalnızca izlenebilirlik içindir.
/// </param>
/// <param name="Notes">Serbest açıklama (opsiyonel).</param>
public sealed record FulfillmentEntry(
    int RequestLineId,
    FulfillmentSourceKind Kind,
    int RefDocId,
    decimal Quantity,
    int? RefDocLineId = null,
    string? Notes = null);
