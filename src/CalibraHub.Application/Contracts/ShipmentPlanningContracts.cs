namespace CalibraHub.Application.Contracts;

/// <summary>
/// Yükleme Planlama Merkezi + Stok Rezervasyonu — Faz 1 (2026-07-28).
/// İhtiyaç Karşılama Merkezi'nin (Fulfillment Center) satış-tarafı analoğu: açık satış
/// siparişi kalemlerini listeler, seçilen kalemler için MANTIKSAL stok rezervasyonu
/// (StockReservation, fiziksel stoğu azaltmaz) oluşturur/iptal eder.
///
/// Faz 1 KAPSAMI: kit-DIŞI (Items.TypeId != 10 / ItemTypeCatalog.IsKit) normal malzemeler,
/// miktar düzeyi. Kit satırları listede görünür (<see cref="OpenOrderLineForReservationDto.IsKit"/>)
/// ama rezerve edilemez — bkz. IStockReservationRepository.CreateReservationsAsync.
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
    /// <summary>Items.TypeId = 10 (Kit) ise true — Faz 1'de rezerve edilemez.</summary>
    bool IsKit,
    /// <summary>Rezervasyonun varsayılan hedef deposu (DocumentLine.LocationId ?? Document.LocationId).</summary>
    int? LocationId,
    string? LineNotes);

public sealed record StockReservationDto(
    int Id,
    int OrderDocumentId,
    int OrderLineId,
    int ItemId,
    string? MaterialCode,
    string? MaterialName,
    int LocationId,
    string? LocationName,
    /// <summary>Gösterim birimindeki rezerve miktar.</summary>
    decimal Quantity,
    /// <summary>Kanonik (baz-birim) rezerve miktar.</summary>
    decimal BaseQuantity,
    /// <summary>1=Active, 2=Shipped, 3=Cancelled.</summary>
    byte Status,
    DateTime? PlannedShipDate,
    string? Notes,
    DateTime Created);

/// <summary>Tek bir sipariş kalemi için rezervasyon talebi (gösterim biriminde miktar — kalemin kendi UnitId'si).</summary>
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
