using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Birleşik kalem-eşleşme (DocumentLineLink) persistence — bkz. tasarım dökümanı
/// "Birleşik Kalem-Eşleşme Tablosu (DocumentLineLink)".
///
/// FAZ 0 İSKELET (2026-07-20): bu arayüzü implemente eden repo DI'a kayıtlıdır ama hiçbir
/// servis/controller tarafından ÇAĞRILMIYOR. Faz 1'de mevcut üç mekanizmanın (SourceLineId /
/// WorkOrderSource / DocumentLineFulfillment) dual-write adaptörü buraya bağlanacak; okuma
/// tarafı (ComputeLineFloor birleşik hali, İlişkili Kalemler UI) Faz 2'de bağlanır.
///
/// Yön her zaman kaynak satır → hedef (tasarım KN-1). Okuma yolları WHERE IsActive=1
/// filtrelidir (tasarım §3 index'leri).
/// </summary>
public interface IDocumentLineLinkRepository
{
    /// <summary>
    /// Tek bir bağlantı kaydı ekler. Faz 0'da hiçbir yerden çağrılmıyor; Faz 1'de mevcut üç
    /// mekanizmanın yazdığı her noktaya (SaveLinesAsync / WorkOrderSource insert /
    /// FulfillmentLedger.InsertEntriesAsync) paralel eklenecek adaptör çağrısı için hazırlandı.
    /// </summary>
    Task InsertAsync(DocumentLineLinkEntry entry, int? userId, CancellationToken ct);

    /// <summary>Bir kaynak satırın AKTİF bağlantı kayıtlarını döner (SourceLineId index'i üzerinden).</summary>
    Task<IReadOnlyList<DocumentLineLink>> GetBySourceLineAsync(int sourceLineId, CancellationToken ct);

    /// <summary>
    /// Bir hedef belgeye (<paramref name="targetDocId"/>) ait AKTİF bağlantı kayıtlarını
    /// pasifleştirir — hedef belge silinince/iptal edilince çağrılır. İş emri link'lerinde de
    /// TargetDocId dolu olduğundan (tasarım KN-2: WorkOrder.DocumentId ile doldurulur) tek
    /// parametre hem ticari belge hem iş emri iptalini kapsar. FulfillmentLedger.ReverseByDocumentAsync
    /// ile aynı desen: tür filtresi YOK, yalnız TargetDocId ile çalışır — bir hedef Id'si tek
    /// bir belgeye ait olduğundan çakışma riski yoktur. Etkilenen kayıt sayısını döner.
    /// </summary>
    Task<int> ReverseByTargetAsync(int targetDocId, int? userId, CancellationToken ct);

    /// <summary>
    /// FAZ 1a (2026-07-20) — bir kaynak satırın (<paramref name="sourceLineId"/>) link
    /// tablosundan taban (floor) bileşenlerini döner: tasarım §5 "Floor hesabı yeni tabloda"
    /// birebir. Üç bileşen AYRIDIR ve bu metotta TOPLANMAZ:
    /// <list type="bullet">
    /// <item><description><c>Consumed</c> — karşılama kovası, LinkType 1-7 toplamı (İhtiyaç Kaydı satırı için; bugünkü FulfilledFromStock+FulfilledByPurchase eşdeğeri).</description></item>
    /// <item><description><c>Derived</c> — dönüşüm zinciri, yalnız LinkType 10 toplamı (bugünkü GetDerivedLineAggregatesAsync eşdeğeri).</description></item>
    /// <item><description><c>WorkOrder</c> — iş emri tahsisi, yalnız LinkType 20 toplamı (bugünkü GetAllocatedQuantitiesForDocumentAsync eşdeğeri).</description></item>
    /// </list>
    /// LinkType 21/22 (üretim sarf/mamul) kasıtlı olarak HARİÇ — bunlar sipariş floor'una
    /// girmez (tasarım §4). Yalnız <c>IsActive=1</c> kayıtlar sayılır. Floor'un kendisi
    /// (MAX(Consumed,Derived,WorkOrder)) bu metotta hesaplanmaz — ÇAĞIRAN taraf hesaplar
    /// (bkz. <c>DocumentService.ComputeLineFloor</c>). Faz 1a'da yalnız çapraz-doğrulama
    /// için okunur; hiçbir yazma/servis akışına henüz bağlanmadı.
    /// </summary>
    Task<(decimal Consumed, decimal Derived, decimal WorkOrder)> GetFloorComponentsAsync(int sourceLineId, CancellationToken ct);

    /// <summary>
    /// <see cref="GetFloorComponentsAsync"/> ile birebir aynı üç bileşeni, bir belgenin
    /// (<paramref name="documentId"/>) TÜM kaynak satırları için TEK sorguda döner (N+1
    /// önleme) — <c>SqlWorkOrderRepository.GetAllocatedQuantitiesForDocumentAsync</c> ile
    /// aynı desen: <c>SourceDocId</c> üzerinden filtrelenip <c>SourceLineId</c>'ye göre
    /// gruplanır. Sözlükte olmayan bir <c>sourceLineId</c> için üç bileşen de 0 kabul edilir
    /// (o satırın hiç link'i yok demektir). Faz 1a çapraz-doğrulama belge bazlı akışlarda
    /// (SaveQuoteAsync / GetLineLocksAsync eşdeğeri) bunu kullanacak.
    /// </summary>
    Task<IReadOnlyDictionary<int, (decimal Consumed, decimal Derived, decimal WorkOrder)>> GetFloorComponentsForDocumentAsync(int documentId, CancellationToken ct);
}
