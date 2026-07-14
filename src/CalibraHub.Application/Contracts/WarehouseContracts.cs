namespace CalibraHub.Application.Contracts;

// ── Belge başlığı DTO ─────────────────────────────────────────────────────────
public sealed record StockDocDto(
    int Id,
    int CompanyId,
    string DocType,
    string DocNo,
    DateTime DocDate,
    int? FromLocationId,
    string? FromLocationName,
    int? ToLocationId,
    string? ToLocationName,
    string? RefNo,
    string? Notes,
    int? CreatedById,
    DateTime Created,
    bool IsActive,
    int LineCount,
    int? ArgeProjectId,
    string? ArgeProjectName);

// ── Kalem DTO ─────────────────────────────────────────────────────────────────
public sealed record StockDocLineDto(
    int Id,
    int DocId,
    int LineNo,
    int ItemId,
    string? MaterialCode,
    string? MaterialName,
    int? UnitId,
    string? UnitCode,
    decimal Qty,
    int? CombinationId,
    string? CombinationCode,
    string? Notes,
    int? FromLocationId,
    string? FromLocationName,
    int? ToLocationId,
    string? ToLocationName,
    decimal? UnitCost,
    string? LotNo = null,
    // Seri takibi Faz 2: satıra bağlı seri no'lar + edit UI'ının takip modunu bilmesi için
    // stok kartı bilgisi (TrackingType/AutoSerial — Items join'inden).
    IReadOnlyList<string>? Serials = null,
    string? TrackingType = null,
    bool AutoSerial = false);

// ── Kayıt istekleri ───────────────────────────────────────────────────────────
public sealed record SaveStockDocRequest(
    int? Id,
    string DocType,
    string? DocNo,
    DateTime DocDate,
    int? FromLocationId,
    int? ToLocationId,
    string? RefNo,
    string? Notes,
    IReadOnlyList<SaveStockDocLineRequest> Lines,
    int? ArgeProjectId);

public sealed record SaveStockDocLineRequest(
    int? Id,
    int? ItemId,
    string? MaterialCode,
    string? MaterialName,
    int? UnitId,
    decimal Qty,
    int? CombinationId,
    string? Notes,
    int? FromLocationId,
    int? ToLocationId,
    decimal? UnitCost,
    string? LotNo = null,
    // Seri-takipli stokta satırın seri no listesi. Girişte AutoSerial=1 ise boş bırakılabilir
    // (sunucu üretir); diğer tüm durumlarda adet kadar seri zorunludur.
    IReadOnlyList<string>? Serials = null,
    // Sayım — lot-takipli kalemde çoklu lot kırılımı ([{LotNo, Qty}]). CountedQty = Qty toplamı.
    IReadOnlyList<StockLotBreakdownItem>? LotBreakdown = null,
    // Sayım — seri-takipli kalemde zengin seri kırılımı ([{SerialNo, ExpiryDate, Description, Qty}]).
    // Seri = parti (miktar serbest); CountedQty = Qty toplamı. Uygula'da Lot(SKT)+ItemSerial'a yansır.
    IReadOnlyList<CountSerialBreakdownItem>? SerialBreakdown = null);

/// <summary>Sayım lot kırılımı satırı — bir kalemde birden fazla lot sayılabilir.</summary>
public sealed record StockLotBreakdownItem(string LotNo, decimal Qty);

/// <summary>Sayım seri kırılımı satırı — seri no + SKT + açıklama + miktar (seri = parti, miktar serbest).
/// Uygula'da SKT → Lot.ExpiryDate, açıklama → ItemSerial kaydına yazılır.</summary>
public sealed record CountSerialBreakdownItem(string SerialNo, DateTime? ExpiryDate, string? Description, decimal Qty);

// ── Üretim sarfı (iş emri bileşen sarfı, 2026-07-10) ─────────────────────────
// Üretilen miktara göre bileşen sarfı: reçete önerisi + serbest satır. Satır bir
// WorkOrderComponent'e bağlıysa Id'si gelir (IssuedQuantity artar); serbest satırda
// null → bileşen kaydı yoksa RequiredQuantity=0 "Serbest sarf" satırı açılır.
// Lot/seri kuralları stok çıkışıyla AYNI: lot-takipli satırda mevcut lot zorunlu,
// seri-takipli satırda stoktaki (InStock) serilerden adet kadar seçim zorunlu.
public sealed record WorkOrderConsumptionLineRequest(
    int? WorkOrderComponentId,
    int? ItemId,
    string? MaterialCode,
    int? UnitId,
    decimal Qty,
    int? CombinationId,
    int? FromLocationId,        // null → iş emrinin deposu (WarehouseLocationId)
    string? LotNo = null,
    IReadOnlyList<string>? Serials = null,
    string? Notes = null);

public sealed record WorkOrderConsumptionRequest(
    int WorkOrderId,
    decimal? ProducedQuantity,  // bilgi amaçlı — satır notuna işlenir
    IReadOnlyList<WorkOrderConsumptionLineRequest> Lines);
