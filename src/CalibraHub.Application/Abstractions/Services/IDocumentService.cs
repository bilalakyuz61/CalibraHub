using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IDocumentService
{
    Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesAsync(string? search, string? status, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentListItemDto>> GetQuotesByContactAsync(int contactId, CancellationToken ct);

    /// <summary>Belge tipi kodu ile filtrelenmis liste — orn. "satis_siparisi" siparis ekrani icin.</summary>
    Task<IReadOnlyCollection<DocumentListItemDto>> GetByTypeAsync(string typeCode, string? search, string? status, CancellationToken ct);

    /// <summary>Siparise donusturulebilir teklifleri filtrele — modal listesi icin.</summary>
    Task<IReadOnlyCollection<DocumentListItemDto>> GetConvertibleQuotesAsync(
        DateTime? fromDate, DateTime? toDate, int? contactId, string? search, CancellationToken ct);

    /// <summary>
    /// Secili teklifleri cari bazinda gruplayip her cari icin tek bir siparis (Document, type=satis_siparisi) uretir.
    /// Kaynak teklifin durumu Converted'a geciler, document_source koprusu kayit eklenir.
    /// </summary>
    Task<CreateOrdersFromQuotesResult> CreateOrdersFromQuotesAsync(
        CreateOrdersFromQuotesRequest req, int? createdById, CancellationToken ct);
    /// <summary>
    /// Genel belge dönüşümü (satın alma zinciri: talep→teklif→sipariş). Kaynak belgeleri cari
    /// bazında gruplayıp her grup için hedef türünde tek belge üretir; kalemleri SourceLineId ile
    /// klonlar, DocumentSource köprüsü ekler, kaynağı Converted yapar. requireApproved: kaynak
    /// Approved olmalı mı; requireContact: kaynakta cari zorunlu mu (talep→teklif'te false).
    /// </summary>
    Task<CreateOrdersFromQuotesResult> ConvertDocumentsAsync(
        IReadOnlyCollection<int> sourceIds, string sourceTypeCode, string targetTypeCode,
        bool requireApproved, bool requireContact, DateTime targetDate, int? createdById, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentListItemDto>> GetMovementsByContactAsync(int contactId, int? documentTypeId, DateTime? fromDate, DateTime? toDate, CancellationToken ct);
    Task<DocumentDto?> GetQuoteByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyCollection<DocumentLineDto>> GetQuoteLinesAsync(int documentId, CancellationToken ct);

    /// <summary>
    /// Belgenin bağlantı-kilitli satırları (düzenleme ekranı önden koruması için).
    /// Karşılanmış (İhtiyaç) veya türetilmiş aktif belge satırı olan her satır için
    /// silme engeli + miktar tabanı (MinQuantity) döner; kilitli olmayan satır listede yer almaz.
    /// <see cref="SaveQuoteAsync"/> aynı tabanı sunucuda zorlar (tek formül) — bu UI önizlemesidir.
    /// </summary>
    Task<IReadOnlyList<DocumentLineLockDto>> GetLineLocksAsync(int documentId, CancellationToken ct);

    /// <summary>Bir kit satirinin DONMUS icerik snapshot'ini (bilesen kod/ad/miktar) doner (Faz 2).
    /// Grid'de kit satirinin acilir dokumu + Faz 3 patlatma icin. Kit satiri degilse bos liste.</summary>
    Task<IReadOnlyCollection<CalibraHub.Domain.Entities.DocumentLineKitComponent>> GetKitLineComponentsAsync(
        int documentLineId, CancellationToken ct);
    /// <summary>
    /// <paramref name="fulfillmentEntries"/> (2026-07-20, Madde 2) — verilirse, bu belgenin
    /// karşıladığı İhtiyaç Kaydı satırları defter kaydı belgenin kendi kalem yazım transaction'ı
    /// İÇİNDE yapılır (belge kaydı ile defter yazımı atomik olur). Yalnız yeni belge kaydında
    /// (RequestIds → yeni sipariş/talep) anlamlıdır; controller'lar (PurchaseController)
    /// <see cref="PendingFulfillmentEntry"/> listesini kaydetmeden ÖNCE hazırlar (RefDocId
    /// henüz bilinmediği için taşımaz — repo, kendi ürettiği belge Id'sini kullanır).
    /// </summary>
    Task<(bool Success, string? Error, DocumentDto? Quote, bool ApprovalStarted)> SaveQuoteAsync(
        SaveDocumentRequest request, int? createdById, string? startedByUser, CancellationToken ct,
        IReadOnlyCollection<PendingFulfillmentEntry>? fulfillmentEntries = null);
    /// <summary>
    /// Belge silinebilir mi — silinebilirse <c>null</c>, silinemezse kullanıcıya
    /// gösterilecek gerekçe mesajını döner (karşılanmış kalem / türetilmiş aktif
    /// belge). <see cref="DeleteQuoteAsync"/> silmeden önce bunu çağırır; UI de
    /// silme onayını göstermeden ÖNCE çağırarak engelli belgede onay yerine
    /// doğrudan uyarı gösterebilir (tek kaynak — mesajlar birebir aynı).
    /// </summary>
    Task<string?> GetDeleteBlockReasonAsync(int id, CancellationToken ct);

    /// <summary>
    /// Belgeyi soft-delete eder. Karşılanmış kalem içeriyorsa (İhtiyaç Kaydı zinciri
    /// koruması) veya türetilmiş aktif belgesi varsa silinmez → (false, mesaj) döner.
    /// Aynı kontrol <see cref="GetDeleteBlockReasonAsync"/> üzerinden yapılır.
    /// </summary>
    Task<(bool Ok, string? Error)> DeleteQuoteAsync(int id, CancellationToken ct);
    Task<(bool Success, string? Error)> ChangeStatusAsync(int id, string newStatus, CancellationToken ct);
    Task<string> GetNextDocumentNumberAsync(CancellationToken ct);

    /// <summary>
    /// Satir revizyonu — eski satirin notes alanini @description ile gunceller,
    /// yeni satir eski'nin birebir kopyasi olarak eklenir (revised_from_id baglantisi
    /// ile). Return: yeni satirin Id'si; parent bulunamazsa null.
    /// Widget (SALES_QUOTE_LINES form) degerlerinin kopyalanmasi controller
    /// tarafinda WidgetService ile yapilir (schema dinamik).
    /// </summary>
    Task<int?> ReviseLineAsync(int parentLineId, string? description, CancellationToken ct);

    /// <summary>
    /// 2026-07-18 — Satirin bagli oldugu belge Id'si (satir yoksa null). Satir-bazli
    /// endpoint'lerin yetkiyi BELGE tipinden cozebilmesi icin eklendi.
    /// </summary>
    Task<int?> GetDocumentIdByLineAsync(int lineId, CancellationToken ct);

    /// <summary>
    /// 2026-07-20 (Madde 3 — audit) — Bir belgenin (<paramref name="refDocId"/>) karşılama
    /// defterinde şu an AKTİF katkısı olan İhtiyaç Kaydı satır Id'lerini döner. Silme/ters
    /// çevirme ÖNCESİ audit "eski durum" snapshot'ı almak için — salt-okunur ön-kontrol.
    /// </summary>
    Task<IReadOnlyList<int>> GetFulfillmentAffectedLineIdsAsync(int refDocId, CancellationToken ct);

    /// <summary>
    /// 2026-07-20 (Madde 3) — Verilen satır Id'lerinin (farklı belgelere ait olabilir) o anki
    /// DTO snapshot'ını döner (Id → DocumentLineDto). Bulunamayan Id'ler sonuçta yer almaz.
    /// Audit "eski durum" (mutasyondan ÖNCE) veya "yeni durum" (mutasyondan SONRA) okumak için
    /// kullanılır — <see cref="LogFulfillmentAuditAsync"/> ile birlikte.
    /// </summary>
    Task<IReadOnlyDictionary<int, DocumentLineDto>> GetLinesSnapshotAsync(
        IReadOnlyCollection<int> lineIds, CancellationToken ct);

    /// <summary>
    /// 2026-07-20 (Madde 3) — Karşılama/ters çevirme sonrası etkilenen İhtiyaç Kaydı
    /// satırlarının audit log'unu yazar. <paramref name="oldLinesById"/> mutasyondan ÖNCE
    /// alınmış snapshot'tır (bkz. <see cref="GetLinesSnapshotAsync"/> veya çağıranın zaten
    /// elinde bulunan veri — ör. PurchaseController'ın karşılama öncesi çektiği İhtiyaç
    /// satırları); yeni durum burada DB'den taze okunur. Satırlar DocumentId'ye göre gruplanıp
    /// entity="alis_talebi" + recordId=İhtiyaç belgesinin Id'si ile loglanır — bir işlem birden
    /// çok İhtiyaç belgesini etkileyebilir (çoklu RequestIds/FulfillFromStock). Audit servisi
    /// yoksa veya hata olursa sessizce no-op (işlem akışını asla bozmaz).
    /// </summary>
    Task LogFulfillmentAuditAsync(
        IReadOnlyDictionary<int, DocumentLineDto> oldLinesById, string detail, CancellationToken ct);

    /// <summary>
    /// 2026-07-21 (PageComment Seq 19) — Belge kopyalama: kaynak belgenin başlığı ve
    /// kalemleri (+ kalem detayları) yeni bir Draft belgeye klonlanır. <see cref="SaveQuoteAsync"/>'in
    /// yeni-kayıt (Id=null) yolunu AYNEN kullanır — ikinci bir insert yolu icat edilmez; bu sayede
    /// yeni DocumentNumber (numaralandırma servisinden), Status=Draft, audit LogInsert, kit
    /// snapshot ve onay akışı otomatik başlatma davranışları TUTARLI kalır (tek kaynak).
    /// Karşılama/rezervasyon/seri/DocumentLineLink verileri KOPYALANMAZ: <see cref="SaveDocumentLineRequest"/>
    /// bu alanları zaten taşımaz (Id=null → satır fresh INSERT olur, Fulfilled*/FulfillmentStatus
    /// domain'de 0 başlar) ve <c>Serials</c> bilinçli olarak null verilir (seri rezervasyonu
    /// tetiklenmez). DocumentDate=bugün; diğer başlık alanları (ValidUntil, DeliveryDate, ödeme/
    /// teslim şartları, notlar, cari/lokasyon/temsilci...) kaynaktan BİREBİR taşınır. document_source
    /// köprüsü KURULMAZ (FromRequestId=null) — kopya, kaynağın "türevi" değil bağımsız bir belgedir.
    /// Widget/EAV (Ek Alanlar) değerlerinin kopyalanması bu metotta YAPILMAZ — <see cref="ReviseLineAsync"/>
    /// ile aynı sebep (form şeması dinamik, IWidgetRepository controller katmanında) controller
    /// IWidgetRepository.CopyValuesAsync ile header + her satır için ayrıca kopyalar.
    /// Kaynak bulunamazsa veya kalemsizse (false, mesaj, null) döner.
    /// </summary>
    Task<(bool Success, string? Error, DocumentDto? NewDocument)> CopyDocumentAsync(
        int sourceId, int? createdById, string? startedByUser, CancellationToken ct);
}
