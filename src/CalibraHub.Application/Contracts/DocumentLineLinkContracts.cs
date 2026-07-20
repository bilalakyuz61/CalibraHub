namespace CalibraHub.Application.Contracts;

/// <summary>
/// FAZ 0 İSKELET (2026-07-20) — bkz. tasarım dökümanı "Birleşik Kalem-Eşleşme Tablosu
/// (DocumentLineLink)" §4. Bu enum HİÇBİR okuma/yazma akışına bağlı DEĞİLDİR; şema
/// (dbo.DocumentLineLink, TINYINT [LinkType] kolonu) paralel olarak db ajanı tarafından
/// CalibraDatabaseInitializer'a ekleniyor. Faz 1'de mevcut üç mekanizmanın (SourceLineId /
/// WorkOrderSource / DocumentLineFulfillment) dual-write adaptörü bu enum'u kullanacak.
///
/// Değerler 1-7, bugünkü <see cref="FulfillmentSourceKind"/> ile SAYISAL OLARAK aynıdır
/// (İhtiyaç Kaydı karşılama kovası + izlenebilirlik anlamı birebir korunur) ama bu enum
/// ONDAN BAĞIMSIZDIR — <see cref="FulfillmentSourceKind"/>'a dokunulmadı, ikisi paralel
/// yaşar. 10/20/21/22 değerleri yeni eşleşme türleri için (dönüşüm zinciri + iş emri) — bkz.
/// tasarım §2 mekanizma haritası A ve B.
/// </summary>
public enum LinkType : byte
{
    // ── Karşılama (İhtiyaç Kaydı bucket'ı — bugün DocumentLineFulfillment/FulfillmentSourceKind) ──

    /// <summary>Depolar arası transfer fişi ile karşılama. FulfillmentSourceKind.Transfer ile aynı sayısal değer.</summary>
    Transfer = 1,

    /// <summary>Satın alma teklifi ile karşılama. FulfillmentSourceKind.PurchaseQuote ile aynı sayısal değer.</summary>
    PurchaseQuote = 2,

    /// <summary>Satın alma siparişi ile karşılama. FulfillmentSourceKind.PurchaseOrder ile aynı sayısal değer.</summary>
    PurchaseOrder = 3,

    /// <summary>Ambar çıkış fişi ile karşılama. FulfillmentSourceKind.StockIssue ile aynı sayısal değer.</summary>
    StockIssue = 4,

    /// <summary>Satın alma talebi ile karşılama. FulfillmentSourceKind.PurchaseDemand ile aynı sayısal değer.</summary>
    PurchaseDemand = 5,

    /// <summary>Defter öncesi oluşmuş stok karşılamasının temel çizgisi. FulfillmentSourceKind.LegacyStock ile aynı sayısal değer.</summary>
    LegacyStock = 6,

    /// <summary>Defter öncesi oluşmuş satın alma karşılamasının temel çizgisi. FulfillmentSourceKind.LegacyPurchase ile aynı sayısal değer.</summary>
    LegacyPurchase = 7,

    // ── Dönüşüm zinciri (bugün DocumentLine.SourceLineId — tasarım §2 satır A) ──

    /// <summary>
    /// Teklif→sipariş→irsaliye dönüşüm zinciri (satış + alış, tüm zincir). Bugün
    /// DocumentLine.SourceLineId kolonuyla temsil ediliyor; o kolon KALIR (tasarım KN-4 —
    /// seri çözümleme ve kısmi teslimat doğrudan ona bağlı), bu değer paralel/türetilmiş
    /// bir görünümdür.
    /// </summary>
    Derivation = 10,

    // ── İş emri (bugün WorkOrderSource + üretim belgesi kaydı — tasarım §2 satır B) ──

    /// <summary>Satış siparişi / İhtiyaç Kaydı → İş Emri tahsisi. Bugün WorkOrderSource tablosuyla temsil ediliyor.</summary>
    WorkOrderAlloc = 20,

    /// <summary>İş Emri → Depo Çıkış (hammadde sarfı). Bugün ayrı bir kalem-link olmadan üretim belgesine yazılıyor.</summary>
    WorkOrderConsume = 21,

    /// <summary>İş Emri → Depo Giriş (mamul girişi). Bugün ayrı bir kalem-link olmadan üretim belgesine yazılıyor.</summary>
    WorkOrderProduce = 22,
}

/// <summary>
/// FAZ 0 İSKELET — IDocumentLineLinkRepository (Persistence katmanı) InsertAsync'inin aldığı
/// tek kayıt sözleşmesi: "şu kaynak satırın şu kadarı, şu hedefe bağlandı". Henüz hiçbir
/// servis bu record'u üretip repoya geçmiyor — Faz 1'de mevcut üç mekanizmanın (SourceLineId /
/// WorkOrderSource / DocumentLineFulfillment) yazdığı her noktaya paralel dual-write
/// eklendiğinde kullanılacak.
///
/// <see cref="FulfillmentEntry"/> ile aynı isimlendirme üslubunu takip eder ama BAĞIMSIZ bir
/// sözleşmedir — FulfillmentEntry'ye dokunulmadı.
/// </summary>
/// <param name="LinkType">İlişki türü — değer kümesi için yukarıdaki enum tanımına bakınız.</param>
/// <param name="SourceLineId">Kaynak satır (DocumentLine.Id). Yön her zaman kaynak→hedef (tasarım KN-1).</param>
/// <param name="SourceDocId">Kaynak satırın bağlı olduğu belge (Document.Id) — denormalize, hızlı filtre için.</param>
/// <param name="TargetLineId">Hedef satır (DocumentLine.Id) — irsaliye/çıkış satırı gibi. İş emri tahsis link'lerinde (WorkOrderAlloc) NULL kalır.</param>
/// <param name="TargetDocId">Hedef belge (Document.Id) — iş emri link'lerinde WorkOrder.DocumentId ile doldurulur (tasarım KN-2).</param>
/// <param name="TargetWorkOrderId">Hedef iş emri (WorkOrder.Id) — yalnız iş emri link türlerinde dolu (tasarım KN-2, ayrı IDENTITY uzayı).</param>
/// <param name="Quantity">Bu bağlantının taşıdığı miktar.</param>
/// <param name="Notes">Serbest açıklama (opsiyonel).</param>
public sealed record DocumentLineLinkEntry(
    LinkType LinkType,
    int SourceLineId,
    int SourceDocId,
    int? TargetLineId,
    int? TargetDocId,
    int? TargetWorkOrderId,
    decimal Quantity,
    string? Notes = null);
