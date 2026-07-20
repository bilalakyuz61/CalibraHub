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
}
