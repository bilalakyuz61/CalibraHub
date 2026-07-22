using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IDocumentRepository
{
    Task<IReadOnlyCollection<Document>> GetAllAsync(string? search, string? status, CancellationToken ct);

    /// <summary>
    /// Belge tipi kodu = @typeCode (orn. "satis_teklifi" / "satis_siparisi") olan belgeleri filtrele.
    /// Status / search / tarih araligi opsiyoneldir; CalibraHub teklif vs. siparis listesinde kullanilir.
    /// </summary>
    Task<IReadOnlyCollection<Document>> GetByTypeAsync(string typeCode, string? search, string? status, CancellationToken ct);

    /// <summary>
    /// Siparise donusturulebilir teklifler — Status=Approved, type=satis_teklifi,
    /// daha onceden document_source koprusunde kaynak olarak yer almamis. Opsiyonel
    /// filtreler: tarih araligi, cari, belge no.
    /// </summary>
    Task<IReadOnlyCollection<Document>> GetConvertibleQuotesAsync(
        DateTime? fromDate, DateTime? toDate, int? contactId, string? search, CancellationToken ct);

    Task<Document?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentLine>> GetLinesAsync(int documentId, CancellationToken ct);

    /// <summary>INSERT veya UPDATE. Yeni Id'yi doner (IDENTITY).</summary>
    Task<int> UpsertAsync(Document document, CancellationToken ct);

    /// <summary>
    /// <paramref name="fulfillmentEntries"/> (2026-07-20, Madde 2) — verilirse, karşılama
    /// defteri kayıtları kalem yazımıyla AYNI transaction'da yazılır (belge kaydı ile defter
    /// yazımı atomik olur). RefDocId = <paramref name="documentId"/> (repo İÇİNDE set edilir,
    /// <see cref="PendingFulfillmentEntry"/> bu alanı taşımaz — çağıran zaten documentId'yi
    /// parametre olarak verdiği için ayrıca bilmesi gerekmez).
    /// </summary>
    Task SaveLinesAsync(int documentId, IReadOnlyCollection<DocumentLine> lines, CancellationToken ct,
        int? createdById = null, IReadOnlyCollection<PendingFulfillmentEntry>? fulfillmentEntries = null);
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>Sadece [status] kolonunu günceller — onay başlatıldığında, onaylandığında vb.</summary>
    Task UpdateStatusAsync(int id, string status, CancellationToken ct);
    Task<string> GetNextDocumentNumberAsync(CancellationToken ct);
    Task<IReadOnlyCollection<DocumentLineDetail>> GetLineDetailsAsync(int documentLineId, CancellationToken ct);
    Task SaveLineDetailsAsync(int documentLineId, IReadOnlyCollection<DocumentLineDetail> details, CancellationToken ct);

    // ── Kit snapshot (Faz 2) ─────────────────────────────────────────
    /// <summary>Verilen item id'lerinden AKTIF kit olanlarin icerigini (VersionNo + bilesenler)
    /// doner. Kit olmayan / kit tanimi olmayan id'ler sonuca girmez.</summary>
    Task<IReadOnlyCollection<KitSnapshotSourceDto>> GetActiveKitContentsAsync(
        IEnumerable<int> kitItemIds, CancellationToken ct);

    /// <summary>Verilen belge satiri id'leri icin mevcut kit snapshot anahtarlari (LineId, KitItemId).
    /// Freeze: ayni satirda ayni kit zaten snapshot'landiysa yeniden alinmaz (kit revizyonu gecmis
    /// belgeyi etkilemez). Satirin item'i baska bir kite degistiyse yeniden snapshot alinir.</summary>
    Task<IReadOnlyCollection<(int LineId, int KitItemId)>> GetExistingKitSnapshotsAsync(
        IEnumerable<int> documentLineIds, CancellationToken ct);

    /// <summary>Bir kit satirinin snapshot'ini (DELETE + INSERT) yazar — o anki aktif kit icerigi dondurulur.</summary>
    Task ReplaceKitSnapshotAsync(int documentLineId, int kitItemId, int versionNo,
        IReadOnlyCollection<KitSnapshotComponentDto> components, CancellationToken ct);

    /// <summary>Bir kit satirinin dondurulmus icerigini (enriched: bilesen kod/ad) doner. Faz 3 patlatma + grid gosterimi.</summary>
    Task<IReadOnlyCollection<DocumentLineKitComponent>> GetKitSnapshotAsync(int documentLineId, CancellationToken ct);

    /// <summary>
    /// İhtiyaç Kaydı satırının karşılama miktarlarını günceller ve FulfillmentStatus'ı yeniden hesaplar.
    /// fulfilledFromStock / fulfilledByPurchase kümülatif DELTA değil, yeni toplam değerdir.
    ///
    /// DİKKAT (2026-07-19): karşılama üreten yeni akışlar bunu DEĞİL
    /// <see cref="AddFulfillmentEntriesAsync"/>'i kullanmalıdır. Bu metod toplamı doğrudan
    /// ezer; "hangi belge ne kadar karşıladı" bilgisi tutulmadığı için karşılayan belge
    /// silindiğinde geri düşüm yapılamaz (satır sonsuza dek karşılanmış görünür). Metod
    /// yalnızca defterden yeniden hesaplama yapan iç yol ve geriye dönük onarım için durur.
    /// </summary>
    Task UpdateLineFulfillmentAsync(int lineId, decimal fulfilledFromStock, decimal fulfilledByPurchase, CancellationToken ct);

    /// <summary>
    /// 2026-07-19 — Karşılama defterine (DocumentLineFulfillment) kayıt yazar ve etkilenen
    /// İhtiyaç Kaydı satırlarının toplamlarını DEFTERDEN yeniden hesaplar (tek işlem).
    ///
    /// Neden defter: toplamı doğrudan artırmak "kim neyi karşıladı"yı kaybettirir; karşılayan
    /// belge silinince geri alınamaz. Defter sayesinde ters çevirme
    /// (<see cref="ReverseFulfillmentByDocumentAsync"/>) mümkün olur ve bir satır birden çok
    /// belgeyle (kısmi transfer + kısmi talep) karşılanabilir.
    ///
    /// Durum yeniden hesaplanır; kapatılmış (3) satıra yeni karşılama gelirse satır yeniden
    /// açılır — mevcut <see cref="CloseLineFulfillmentAsync"/> sözleşmesiyle kasıtlı olarak aynı.
    /// </summary>
    /// <returns>Toplamları güncellenen ihtiyaç satırı sayısı.</returns>
    Task<int> AddFulfillmentEntriesAsync(IReadOnlyCollection<FulfillmentEntry> entries, int? userId, CancellationToken ct);

    /// <summary>
    /// 2026-07-20 (Madde 1 — karşılayan belge düzenleme koruması) — Verilen belgenin
    /// (<paramref name="refDocId"/>) karşılama defterindeki AKTİF katkısını malzeme (ItemId)
    /// bazında toplar: DocumentLineFulfillment.RequestLineId → DocumentLine.ItemId join'i ile
    /// "bu belge hangi malzemeden ne kadar İhtiyaç karşılıyor" sorusuna cevap verir.
    ///
    /// Kaynak taraftaki (İhtiyaç Kaydı'nın kendi satırı) korumasıyla (SaveQuoteAsync,
    /// isPurchaseRequest guard'ı) AYNI ilkenin karşılayan belge tarafındaki uygulaması: bu
    /// belge düzenlenirken malzeme bazında miktar döndürülen değerin altına düşürülemez.
    /// Boş sözlük = bu belgenin defterde hiç katkısı yok → düzenleme serbest.
    /// </summary>
    Task<IReadOnlyDictionary<int, (string? MaterialCode, string? MaterialName, decimal FloorQty)>>
        GetFulfillmentContributionByItemAsync(int refDocId, CancellationToken ct);

    /// <summary>
    /// 2026-07-20 (Madde 3 — audit) — Bu belgenin (<paramref name="refDocId"/>) karşılama
    /// defterinde şu an AKTİF katkısı olan İhtiyaç Kaydı satır Id'lerini (RequestLineId,
    /// distinct) döner. Silme/ters çevirme ÖNCESİ audit "eski durum" snapshot'ı almak için
    /// kullanılır — salt-okunur bir ön-kontroldür, ReverseByDocumentAsync'in davranışına
    /// dokunmaz (aynı WHERE koşulunu paylaşır).
    /// </summary>
    Task<IReadOnlyList<int>> GetFulfillmentAffectedLineIdsAsync(int refDocId, CancellationToken ct);

    // NOT: ters çevirmenin (karşılama belgesi silinince katkıyı geri alma) burada bir
    // sözleşmesi YOKTUR — bilinçli. İşlem, silme ile AYNI transaction'da yapılmak zorunda
    // olduğu için doğrudan repo DeleteAsync'lerinin içinde, paylaşılan
    // Persistence/Repositories/FulfillmentLedger.ReverseByDocumentAsync ile çalışır.
    // Ayrı bir repo metodu olsaydı kendi bağlantısını açar ve "belge silindi ama karşılama
    // geri alınmadı" yarım durumuna kapı aralardı.

    /// <summary>
    /// 2026-07-19 — İhtiyaç Kaydı satırının KALAN miktarını kapatır: FulfillmentStatus = 3
    /// (Cancelled). Karşılanan miktarlar (FulfilledFromStock/ByPurchase) KORUNUR; kapatma
    /// "bu satır için artık bir şey yapılmayacak" demektir, karşılananı geri almaz.
    ///
    /// Neden ayrı bir metod: mevcut <see cref="UpdateLineFulfillmentAsync"/> durumu iki
    /// toplamdan yeniden hesaplar ve yalnız 0/1/2 üretir — 3'ü hiçbir zaman yazmaz.
    /// Belge-seviyesi toptan iptal (eski CloseRequests sweep'i, 2026-07-22'de kaldırıldı)
    /// aynı belgedeki diğer kalemi de vururdu; senaryo (bir kalemin kalanını kapat,
    /// diğerine dokunma) satır-seviyesi bu metodu gerektirir.
    ///
    /// NOT: kapatılmış satıra sonradan karşılama gelirse UpdateLineFulfillmentAsync durumu
    /// yeniden hesaplayıp 3'ü siler — bu KASITLIDIR, satır yeniden açılmış sayılır.
    /// </summary>
    /// <returns>Durumu güncellenen satır sayısı.</returns>
    Task<int> CloseLineFulfillmentAsync(IReadOnlyCollection<int> lineIds, CancellationToken ct);

    /// <summary>
    /// Belgenin satırlarına SourceLineId ile referans veren (AKTİF belgelerdeki) türetilmiş
    /// satır agregatları: kaynakLineId → (türetilmiş satır sayısı, toplam miktar).
    /// Bağlantı bütünlüğü guard'ları için: referanslı kaynak kalem silinemez, miktarı
    /// türetilmiş toplamın altına düşürülemez (teklif→sipariş vb. zincirler).
    /// </summary>
    Task<IReadOnlyDictionary<int, (int Count, decimal QtySum)>> GetDerivedLineAggregatesAsync(int documentId, CancellationToken ct);

    /// <summary>
    /// Bir satiri revize et — atomik olarak:
    ///   1) Eski satirin notes = @description (revize gerekcesi / eski halin anlatimi)
    ///   2) Yeni satir: eski satirin birebir kopyasi + revised_from_id = parentLineId
    ///   3) Kombinasyon detaylari da yeni satira kopyalanir (cascade preserve).
    /// Widget degerleri (WidgetService) controller tarafinda ayrica kopyalanir.
    /// Return: yeni satirin Id'si (kullanici arayuzu ve widget kopyasi icin).
    /// Parent bulunamazsa null doner.
    /// </summary>
    Task<int?> ReviseLineAsync(int parentLineId, string? description, CancellationToken ct);

    /// <summary>
    /// 2026-07-18 — Bir satirin bagli oldugu belge Id'sini doner (satir yoksa null).
    /// Yetkilendirme icin eklendi: satir-bazli endpoint'lerde (ReviseLine) izin, satirin
    /// ait oldugu BELGENIN tipinden cozulmelidir; tip istemci beyanindan alinamaz.
    /// </summary>
    Task<int?> GetDocumentIdByLineAsync(int lineId, CancellationToken ct);

    /// <summary>
    /// Append-only: DocumentLine'a TEK yeni stok hareketi satırı ekler (LineNo = mevcut
    /// max+1, UPDLOCK+HOLDLOCK ile concurrent-safe). SaveLinesAsync'in upsert-all/replace
    /// davranışından FARKLIDIR — mevcut satırları etkilemez, sadece INSERT yapar. WorkOrder
    /// üretim olayları ve Sayım "Yansıt" fark satırları için kullanılır. Yeni satırın Id'sini döner.
    /// </summary>
    Task<int> AppendStockLineAsync(int documentId, DocumentLine line, CancellationToken ct);
}
