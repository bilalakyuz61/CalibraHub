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
    /// Seçilen sipariş kalemleri için rezervasyon oluşturur (tek transaction). Kit satırı, açık miktarı
    /// aşan talep veya yetersiz kullanılabilir stok içeren kalemler <c>Skipped</c> listesine reason ile
    /// düşer — TÜM istek reddedilmez (kısmi başarı Fulfillment deseniyle aynı).
    /// </summary>
    Task<CreateReservationResult> CreateReservationsAsync(
        CreateReservationRequest request, int? userId, CancellationToken ct);

    /// <summary>Yalnız Status=Active olan rezervasyonları iptal eder (Status=Cancelled, IsActive=0). Döner: iptal edilen kayıt sayısı.</summary>
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
    /// Tek transaction (tüm cari grupları dahil); hata → tam rollback.
    /// </summary>
    Task<ShipReservationsResult> ShipReservationsAsync(
        IReadOnlyList<int> reservationIds, int? userId, CancellationToken ct);
}
