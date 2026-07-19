namespace CalibraHub.Application.Contracts;

/// <summary>
/// Bir İhtiyaç Kaydı satırının hangi tür belgeyle karşılandığı.
/// DocumentLineFulfillment.FulfillmentType (TINYINT) kolonunda saklanır → mevcut sayısal
/// değerler DEĞİŞTİRİLEMEZ, yalnızca yeni değer eklenebilir.
///
/// Türün İKİ ayrı işlevi var, karıştırma:
///   1) <b>Kova eşlemesi</b> — toplam hangi kolona yazılacak (FulfilledFromStock vs
///      FulfilledByPurchase). <see cref="FulfillmentSourceKinds"/> bunu tanımlar.
///   2) <b>İzlenebilirlik</b> — karşılamanın hangi iş akışından geldiği.
///
/// Tür, belgenin hangi TABLOda durduğunu göstermez: 2026-07-02'de stock_doc/stock_doc_line
/// emekliye ayrıldı, transfer ve ambar çıkış fişleri de dbo.Document tablosunda tutuluyor.
/// Yani RefDocId tek bir IDENTITY Id uzayındadır ve her değer tek bir belgeye aittir.
/// Bu yüzden TERS ÇEVİRME türe göre filtrelenMEZ — yalnız RefDocId ile yapılır (gerekçe:
/// Persistence/Repositories/FulfillmentLedger.ReverseByDocumentAsync).
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

    /// <summary>
    /// Defter öncesi (2026-07-19 öncesi) oluşmuş stok karşılamasının temel çizgisi.
    /// RefDocId NULL'dır → hiçbir ters çevirmeye takılmaz, kalıcı taban değeridir.
    /// Gerekçe: toplamlar defterden türetildiği için, defterde kaydı olmayan eski karşılama
    /// 0 katkı sayılırdı; aynı satıra yeni bir karşılama geldiğinde recalc her iki kovayı da
    /// ezip eski miktarı SESSİZCE silerdi.
    /// </summary>
    LegacyStock = 6,

    /// <summary>Defter öncesi oluşmuş satın alma karşılamasının temel çizgisi. Bkz. <see cref="LegacyStock"/>.</summary>
    LegacyPurchase = 7,
}

/// <summary>
/// <see cref="FulfillmentSourceKind"/> aileleri. Ters çevirme ve toplam hesaplama bu
/// gruplamayı kullanır — tek doğruluk kaynağı burasıdır, SQL'e sayı gömülmez.
/// </summary>
public static class FulfillmentSourceKinds
{
    /// <summary>Toplamı DocumentLine.FulfilledFromStock kovasına yazılan türler.</summary>
    public static readonly IReadOnlyList<byte> StockSide = new byte[]
    {
        (byte)FulfillmentSourceKind.Transfer,
        (byte)FulfillmentSourceKind.StockIssue,
        (byte)FulfillmentSourceKind.LegacyStock,
    };

    /// <summary>Toplamı DocumentLine.FulfilledByPurchase kovasına yazılan türler.</summary>
    public static readonly IReadOnlyList<byte> PurchaseSide = new byte[]
    {
        (byte)FulfillmentSourceKind.PurchaseQuote,
        (byte)FulfillmentSourceKind.PurchaseOrder,
        (byte)FulfillmentSourceKind.PurchaseDemand,
        (byte)FulfillmentSourceKind.LegacyPurchase,
    };

    /// <summary>Bu tür stok kovasına mı yazılır?</summary>
    public static bool IsStockSide(FulfillmentSourceKind kind) =>
        kind is FulfillmentSourceKind.Transfer or FulfillmentSourceKind.StockIssue;
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
