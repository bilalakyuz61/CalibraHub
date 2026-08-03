namespace CalibraHub.Application.Contracts;

/// <summary>
/// Yükleme Planlama Merkezi + Stok Rezervasyonu — Faz 1 (2026-07-28), kit desteği Faz 1.5 (2026-08-03).
/// İhtiyaç Karşılama Merkezi'nin (Fulfillment Center) satış-tarafı analoğu: açık satış
/// siparişi kalemlerini listeler, seçilen kalemler için MANTIKSAL stok rezervasyonu
/// (StockReservation, fiziksel stoğu azaltmaz) oluşturur/iptal eder.
///
/// Faz 1.5: Kit (Items.TypeId = 10 / ItemTypeCatalog.IsKit) satırları da tam-SET olarak rezerve
/// edilebilir — bkz. IStockReservationRepository.CreateReservationsAsync kit dalı. Kit satırında
/// <see cref="OrderQty"/>/<see cref="DeliveredQty"/>/<see cref="ReservedQty"/>/<see cref="OpenQty"/>
/// birimi hep "set"tir (kit tanımı gereği miktar birimi yoktur). Kit satırında
/// <see cref="AvailableStock"/> ve <see cref="OpenQty"/> bileşen-kısıtlı set sayısını (bkz.
/// <see cref="AvailableSets"/>/<see cref="ReservedSets"/>) yansıtır; kit-dışı satırlarda her
/// zamanki gibi malzeme bazlı hesaplanır.
/// </summary>
public sealed record OpenOrderLineForReservationDto(
    int LineId,
    int OrderDocumentId,
    string OrderNumber,
    DateTime OrderDate,
    int ItemId,
    string? MaterialCode,
    string? MaterialName,
    int? UnitId,
    string? UnitCode,
    /// <summary>Sipariş kalemi miktarı (gösterim birimi).</summary>
    decimal OrderQty,
    /// <summary>Teslim edilmiş miktar (gösterim birimi).</summary>
    decimal DeliveredQty,
    /// <summary>Bu kaleme bağlı aktif (Status=Active) rezervasyon toplamı (gösterim birimi).</summary>
    decimal ReservedQty,
    /// <summary>Açık miktar = OrderQty − DeliveredQty − ReservedQty (negatif olmaz, 0'a clamp).</summary>
    decimal OpenQty,
    /// <summary>
    /// Kalemin deposundaki (LocationId) kullanılabilir stok (gösterim birimine çevrilmiş) —
    /// fiziksel bakiye − o malzeme+depo için aktif TÜM rezervasyonlar (yalnız bu kalemin değil).
    /// </summary>
    decimal AvailableStock,
    /// <summary>Items.TypeId = 10 (Kit) ise true — Faz 1.5'ten itibaren tam-set olarak rezerve edilebilir.</summary>
    bool IsKit,
    /// <summary>Rezervasyonun varsayılan hedef deposu (DocumentLine.LocationId ?? Document.LocationId).</summary>
    int? LocationId,
    string? LineNotes,
    /// <summary>
    /// Faz 1.5 — yalnız IsKit=true satırlarda dolu (kit-dışı satırda null): bileşen bazında
    /// kısıtlanmış kullanılabilir SET sayısı (min üzerinden, her bileşenin (fiziksel−rezerve)/perKit
    /// tabanı alınıp en darı esas alınır). <see cref="AvailableStock"/> ile AYNI değeri taşır — kit
    /// satırlarında ayrıca "set" etiketiyle göstermek isteyen istemciler bu alanı kullanır.
    /// </summary>
    decimal? AvailableSets = null,
    /// <summary>Faz 1.5 — yalnız IsKit=true satırlarda dolu: bu kit sipariş kalemine bağlı aktif
    /// bileşen rezervasyonlarından türetilen SET sayısı. <see cref="ReservedQty"/> ile AYNI değeri taşır.</summary>
    decimal? ReservedSets = null);

public sealed record StockReservationDto(
    int Id,
    int OrderDocumentId,
    int OrderLineId,
    int ItemId,
    string? MaterialCode,
    string? MaterialName,
    int LocationId,
    string? LocationName,
    /// <summary>Gösterim birimindeki rezerve miktar (kit bileşeninde baz birimle aynı).</summary>
    decimal Quantity,
    /// <summary>Kanonik (baz-birim) rezerve miktar.</summary>
    decimal BaseQuantity,
    /// <summary>1=Active, 2=Shipped, 3=Cancelled.</summary>
    byte Status,
    DateTime? PlannedShipDate,
    string? Notes,
    DateTime Created,
    /// <summary>
    /// Faz 1.5 — NULL ise normal rezervasyon. Dolu ise bu satır bir kit BİLEŞENİNİN rezervasyonudur;
    /// değer = kit sipariş kaleminin (DocumentLine.Id) Id'sidir — aynı KitOrderLineId'ye sahip satırlar
    /// bir arada TEK kit olarak gösterilmelidir (bkz. <see cref="KitSets"/>).
    /// </summary>
    int? KitOrderLineId = null,
    /// <summary>Faz 1.5 — yalnız KitOrderLineId dolu satırlarda anlamlı: bu bileşen satırının
    /// BaseQuantity'sinin kendi kit-başına oranına (perKit) bölünmesiyle türetilen SET sayısı.</summary>
    decimal? KitSets = null);

/// <summary>Tek bir sipariş kalemi için rezervasyon talebi (gösterim biriminde miktar — kalemin kendi
/// UnitId'si; kit satırında (IsKit=true) bu alan SET SAYISI olarak yorumlanır — ayrı bir alan yoktur,
/// ayrım OpenOrderLineForReservationDto.IsKit ile satır bazında yapılır).</summary>
public sealed record CreateReservationLineRequest(int OrderLineId, decimal Qty);

/// <param name="Lines">Rezerve edilecek kalemler.</param>
/// <param name="LocationId">Verilirse tüm kalemler bu depoya rezerve edilir; null ise her kalemin kendi deposu (LocationId ?? Document.LocationId) kullanılır.</param>
public sealed record CreateReservationRequest(
    List<CreateReservationLineRequest> Lines,
    int? LocationId,
    DateTime? PlannedShipDate,
    string? Notes);

public sealed record CreateReservationResultItem(int OrderLineId, decimal Reserved, string? Reason);

public sealed record CreateReservationSkippedItem(int OrderLineId, string Reason);

public sealed record CreateReservationResult(
    bool Ok,
    IReadOnlyList<CreateReservationResultItem> Created,
    IReadOnlyList<CreateReservationSkippedItem> Skipped);

public sealed record CancelReservationRequest(List<int> ReservationIds);

/// <summary>
/// Yükleme Planlama Merkezi — Faz 2 (2026-07-31), "Yükle" aksiyonu. Seçilen aktif
/// (Status=Active) rezervasyonları TAM olarak (kısmi yok) satış irsaliyesine dönüştürür.
/// Cari başına TEK irsaliye üretilir (bkz. IStockReservationRepository.ShipReservationsAsync).
/// Faz 1.5 (2026-08-03): ReservationIds içinde kit bileşen rezervasyonları (KitOrderLineId dolu)
/// varsa, AYNI KitOrderLineId'ye ait TÜM aktif bileşenler seçili olmak ZORUNDADIR — eksikse o
/// kit grubunun tamamı Skipped'e düşer (set bölünmez).
/// </summary>
public sealed record ShipReservationsRequest(List<int> ReservationIds);

/// <summary>Üretilen bir irsaliye özeti (birden çok cari seçiliyse birden çok kayıt döner).</summary>
public sealed record ShipReservationsDeliveryDto(
    int DocumentId,
    string DocNo,
    int ContactId,
    string? ContactName,
    int LineCount);

public sealed record ShipReservationsSkippedItem(int ReservationId, string Reason);

public sealed record ShipReservationsResult(
    bool Ok,
    IReadOnlyList<ShipReservationsDeliveryDto> Deliveries,
    IReadOnlyList<ShipReservationsSkippedItem> Skipped);
