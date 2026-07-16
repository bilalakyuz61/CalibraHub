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
// Lot kırılımı — Seri kırılımıyla aynı zengin düzen (Lot No · SKT · Açıklama · Miktar).
// SKT → Lot.ExpiryDate'e yazılır. ExpiryDate/Description nullable (eski JSON geriye-uyumlu).
public sealed record StockLotBreakdownItem(string LotNo, decimal Qty, DateTime? ExpiryDate = null, string? Description = null);

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

// ── Mobil FIFO teslimat / irsaliye (2026-07-16) ──────────────────────────────
// Mobil Depo, cari + malzeme + miktar alır; FIFO param açıksa miktarı carinin açık
// sipariş satırlarına (aynı malzeme, kalan > 0) belge tarihi/numarası ARTAN sırada tahsis
// eder. Kaynak bağı web kısmi-teslimatıyla BİREBİR AYNI: irsaliye satırı SourceLineId +
// sipariş satırı DeliveredQuantity. Miktarlar ANA BİRİMDE (BaseQuantity) taşınır — mobil
// istemci malzemeyi kartın ana biriminde girer (stok-in/out ile aynı konvansiyon).

/// <summary>Bir malzeme için teslim edilecek miktar. FallbackUnitPrice yalnızca bağlantısız
/// (siparişe düşmeyen) kalan için kullanılır — ResolveLinePrices ile çözülür (yoksa 0).
/// Bağlanan satır fiyatı SİPARİŞ satırından gelir. MaterialName reddedilen/hata mesajları içindir.
/// LOT + SERİ (2026-07-16, mobil V1): <see cref="Serials"/> = istemcinin seçtiği/okuttuğu seri no'lar
/// (satış çıkışında rezerve override / seçim; alış girişinde girilen seriler); <see cref="LotCode"/> =
/// tek lot/satır (V1); <see cref="AutoGenerateSerials"/> = yalnız ALIŞ girişi + AutoSerial kartında
/// sunucunun seri üretmesini ister. Takipsiz malzemede hepsi yok sayılır (repo TrackingType'ı DB'den çözer).</summary>
public sealed record MobileDeliveryLineInput(
    int ItemId, decimal Quantity, int? UnitId, decimal FallbackUnitPrice, string? MaterialName,
    IReadOnlyList<string>? Serials = null, string? LotCode = null, bool AutoGenerateSerials = false);

/// <summary>Bir malzemenin tek bir açık siparişe tahsis edilen (ana birim) miktarı — response özeti.</summary>
public sealed record MobileDeliveryLineLink(string OrderNumber, decimal Quantity);

/// <summary>Bir malzemenin bağlama sonucu: hangi siparişlere ne kadar bağlandı + bağlanamayan kalan.
/// <see cref="Serials"/> = FİİLEN kullanılan seriler (rezerve/override/FIFO çözümü sonrası — istemci
/// buradan gerçek sonucu görür); <see cref="LotCode"/> = uygulanan lot (yoksa null).</summary>
public sealed record MobileDeliveryLineResult(
    int ItemId, IReadOnlyList<MobileDeliveryLineLink> Linked, decimal UnlinkedQuantity,
    IReadOnlyList<string>? Serials = null, string? LotCode = null);

/// <summary>FIFO teslimat sonucu: irsaliye belgesi + kalem bazlı bağlama özeti + (varsa) kaynak
/// sipariş id'leri (controller DocumentSource soyağacı kenarlarını bunlarla yazar).</summary>
public sealed record MobileDeliveryResult(
    int Id, string DocNo,
    IReadOnlyList<int> SourceOrderIds,
    IReadOnlyList<MobileDeliveryLineResult> Lines);
