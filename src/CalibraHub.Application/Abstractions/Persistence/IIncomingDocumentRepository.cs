using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.EDocument;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IIncomingDocumentRepository
{
    Task<bool> ExistsByEnvelopeIdAsync(string envelopeId, CancellationToken cancellationToken);
    Task<bool> ExistsByDocumentNumberAndRecipientAsync(
        string documentNumber,
        string recipientTaxNumber,
        DocumentKind kind,
        CancellationToken cancellationToken);
    /// <param name="details">
    /// Kalem / vergi / tasima satirlari HAZIR verildiginde kullanilir (OFFLINE ERP yolu).
    /// null ise <see cref="IncomingDocument.PayloadRaw"/> icindeki UBL XML ayristirilir
    /// (ONLINE entegrator yolu). Offline kaynakta ayristirilacak XML YOKTUR: Netsis
    /// veritabaninda zarf XML'i (TBLEFATZARF.XMLVERI) pratikte BOSTUR (olculdu: 14.382
    /// satirin tamaminda 1 bayt), veri iliskisel tablolarda durur.
    /// </param>
    Task AddAsync(IncomingDocument document, CancellationToken cancellationToken,
                  EDocumentDetails? details = null);
    Task<IReadOnlyCollection<IncomingDocument>> GetPendingApprovalsAsync(bool? isProcessed, CancellationToken cancellationToken);
    Task<IncomingDocument?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Kuyrugun TEK SAYFASI (tur + islenmis + arama suzgecleriyle, SQL tarafinda).
    ///
    /// <para><b>Neden:</b> ekran bugune kadar TUM belgeleri (olculdu: 10.590 kayit) tek
    /// seferde okuyup ~12 MB JSON uretiyordu; e-Fatura listesi 21 saniyede aciliyordu.
    /// Sayfalama olmadan bu maliyet kayit sayisiyla dogru orantili buyur.</para>
    /// </summary>
    Task<(IReadOnlyList<IncomingDocument> Items, int TotalCount)> GetPendingPageAsync(
        string? kind, bool? isProcessed, string? search, int page, int pageSize,
        CancellationToken cancellationToken);
    Task UpdateIsProcessedAsync(int id, bool isProcessed, CancellationToken cancellationToken);

    /// <summary>
    /// Belgenin ham payload'ini gunceller. Zarf UBL'i sonradan tamamlandiginda kullanilir
    /// (ilk goruntulemede kaynaktan okunur, bir daha okunmasin diye kayda yazilir).
    /// </summary>
    Task UpdatePayloadRawAsync(int id, string payloadRaw, CancellationToken cancellationToken);

    // ── Cari (Contact) eslestirme ────────────────────────────────────────────

    /// <summary>
    /// Gonderen VKN/TC ile TEK bir aktif cariye eslesen, henuz bagli OLMAYAN belgeleri
    /// toplu baglar. Birden cok aday varsa kayit BAGLANMAZ (kullanici secer).
    /// </summary>
    Task<EDocumentContactMatchResultDto> MatchContactsByTaxNumberAsync(CancellationToken cancellationToken);

    /// <summary>Verilen belgelerin bagli cari ozetleri (bagli olmayan belge sozlukte YOKTUR).</summary>
    Task<IReadOnlyDictionary<int, EDocumentContactLinkDto>> GetContactLinksAsync(
        IReadOnlyCollection<int> documentIds, CancellationToken cancellationToken);

    /// <summary>
    /// Bir belgenin cari adaylari. <paramref name="search"/> bossa belgenin VKN/TC'siyle
    /// eslesenler, doluysa kod/unvan/VKN aramasi sonucu doner.
    /// </summary>
    Task<IReadOnlyList<EDocumentContactCandidateDto>> GetContactCandidatesAsync(
        int documentId, string? search, CancellationToken cancellationToken);

    /// <summary>Belgeyi bir cariye baglar; <paramref name="contactId"/> null ise bagi kaldirir.</summary>
    Task UpdateContactAsync(int documentId, int? contactId, CancellationToken cancellationToken);

    /// <summary>
    /// Belgenin YERLI tablolardaki kalemleri (kalem vergileri dahil).
    ///
    /// <para>Ekran eskiden kalemleri her istekte PayloadRaw icindeki UBL XML'ini ayristirarak
    /// uretiyordu. OFFLINE (ERP) kayitlarda ayristirilacak XML YOKTUR — kalemler yalniz bu
    /// tablolarda durur, dolayisiyla okuma buradan yapilmalidir. Kayit yoksa bos liste doner
    /// ve cagiran taraf XML'e geri dusebilir (detay tablolari eklenmeden ONCE aktarilmis
    /// online kayitlar icin).</para>
    /// </summary>
    Task<IReadOnlyList<EDocumentLineData>> GetLinesAsync(int documentId, CancellationToken cancellationToken);

    /// <summary>
    /// Belgenin baslik ek bilgileri (taraflar, toplamlar, belge seviyesi vergiler).
    ///
    /// <para>XML'i olmayan (ERP kaynakli) belgede ekran fatura gorunumunu bunlardan cizer:
    /// ana kayit yalniz gonderici/alici VKN'sini tasir, alicinin adi/adresi ve belge
    /// toplamlari kaynak tablolarda durur. Bilgi yoksa alanlar null doner — cagiran taraf
    /// kalemlerden hesaplamaya geri duser.</para>
    /// </summary>
    Task<EDocumentHeaderExtras?> GetHeaderExtrasAsync(int documentId, CancellationToken cancellationToken);

    /// <summary>
    /// Cevrimdisi kaynaktan ice aktarilmis EN BUYUK ERP anahtari (PayloadRaw icindeki
    /// incKeyNo), belge turune gore. Ilerleme isareti buradan TURETILIR — ayri bir imlec
    /// tablosu tutulmaz, boylece imlec ile veri birbirinden ayrisamaz.
    /// </summary>
    Task<int> GetMaxOfflineSourceKeyAsync(DocumentKind kind, CancellationToken cancellationToken);
}
