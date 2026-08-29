using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Yükleme Planlama Merkezi + Stok Rezervasyonu — Faz 1 (2026-07-28).
/// Bkz. <c>StockReservation</c> entity ve <c>ShipmentPlanningContracts.cs</c> XML doc'ları.
/// </summary>
public interface IStockReservationRepository
{
    /// <summary>Açık (MovementType IS NULL, BaseQuantity &gt; DeliveredQuantity) satış siparişi kalemlerini,
    /// güncel açık/rezerve/kullanılabilir stok bilgisiyle birlikte döner.</summary>
    Task<IReadOnlyList<OpenOrderLineForReservationDto>> GetOpenOrderLinesAsync(
        string? materialSearch, string? orderNumber, CancellationToken ct);

    /// <summary>
    /// Seçilen sipariş kalemleri için rezervasyon oluşturur (tek transaction). Açık miktarı aşan talep
    /// veya yetersiz kullanılabilir stok içeren kalemler <c>Skipped</c> listesine reason ile düşer —
    /// TÜM istek reddedilmez (kısmi başarı Fulfillment deseniyle aynı). Faz 1.5 (2026-08-03): kit
    /// satırlarında (IsKit=true) <c>Qty</c> SET SAYISI olarak yorumlanır ve hepsi-ya-hiç atomik olarak
    /// N bileşen satırı (aynı KitOrderLineId altında) yazılır — bir bileşen bile yetersizse TÜM kit reddedilir.
    /// </summary>
    Task<CreateReservationResult> CreateReservationsAsync(
        CreateReservationRequest request, int? userId, CancellationToken ct);

    /// <summary>
    /// Rezervasyon ÖN KONTROLÜ (2026-08-29) — verilen belge satırları için kullanılabilir stok
    /// yeterli mi? Yetersiz kalan her (malzeme, depo) çifti için bir kayıt döner; liste BOŞ ise
    /// rezervasyon kurulabilir. Teklif satırlarıyla da çalışır (tekliften siparişe dönüşümde,
    /// HİÇBİR ŞEY OLUŞTURULMADAN önce blok kararı verilebilsin diye). Kit satırı bileşenlerine
    /// patlatılır — snapshot yoksa freeze-on-first ile dondurulur (SaveQuoteAsync ile aynı kaynak).
    /// Talep aynı (malzeme, depo) için satırlar arasında TOPLANIR: iki kalem aynı stoğu isterse
    /// tek tek yeterli görünüp toplamda yetersiz kalması engellenir.
    /// </summary>
    Task<IReadOnlyList<StockShortageDto>> CheckLinesAvailabilityAsync(
        IReadOnlyCollection<int> documentLineIds, int? locationId, CancellationToken ct);

    /// <summary>Yalnız Status=Active olan rezervasyonları iptal eder (Status=Cancelled, IsActive=0). Döner:
    /// iptal edilen kayıt sayısı. Faz 1.5: verilen id'lerden biri kit bileşeniyse, aynı KitOrderLineId'ye
    /// ait TÜM aktif bileşenler de otomatik iptal kapsamına genişletilir (set bütünlüğü).</summary>
    Task<int> CancelReservationsAsync(
        IReadOnlyList<int> reservationIds, int? userId, CancellationToken ct);

    /// <summary>Aktif rezervasyon listesi — orderDocumentId ve/veya orderLineId ile filtrelenebilir (ikisi de null ise tüm aktif rezervasyonlar).</summary>
    Task<IReadOnlyList<StockReservationDto>> GetReservationsAsync(
        int? orderDocumentId, int? orderLineId, CancellationToken ct);

    /// <summary>
    /// Faz 2 (2026-07-31) — "Yükle": seçilen aktif rezervasyonların TAMAMINI satış irsaliyesine
    /// dönüştürür (kısmi yükleme yok). Cari (Document.ContactId) başına TEK irsaliyede toplanır;
    /// çıkış deposu rezervasyonun kendi LocationId'sidir (sipariş kaleminin deposu DEĞİL). Rezervasyon
    /// bulunamaz/zaten yüklenmiş/iptal edilmişse o rezervasyon Skipped listesine reason ile düşer —
    /// diğer geçerli rezervasyonlar yine de işlenir (Fulfillment deseni, tüm istek reddedilmez).
    /// Tek transaction (tüm cari grupları dahil); hata → tam rollback. Faz 1.5 (2026-08-03): kit
    /// bileşen rezervasyonları (KitOrderLineId dolu) KİT BAŞLIK satırı (stok etkisiz) + BİLEŞEN
    /// satırları (gerçek çıkış) olarak patlatılır — aynı KitOrderLineId'nin TÜM bileşenleri seçili
    /// değilse o kit grubu tamamen Skipped'e düşer (set bölünmez). Seri takibi hâlâ KAPSAM DIŞI (Faz 3).
    /// </summary>
    Task<ShipReservationsResult> ShipReservationsAsync(
        IReadOnlyList<int> reservationIds, int? userId, CancellationToken ct);
}
