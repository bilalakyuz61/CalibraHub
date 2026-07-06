namespace CalibraHub.Application.Contracts;

/// <summary>
/// Malzeme kartı "Stok Hareketleri" sekmesi — tek hareket satırı.
/// Kaynak: DocumentLine (MovementType dolu) + Document başlığı. StockMovement tablosu
/// 2026-07-02'de emekliye ayrıldı. Yön/işaret, hangi lokasyon alanının dolu olduğuna
/// göre belirlenir (SqlInventoryCountRepository bakiye formülüyle aynı):
/// Giriş = ToLocationId dolu (+), Çıkış = FromLocationId dolu (−), Transfer = ikisi de (net 0).
/// </summary>
public sealed record ItemStockMovementRowDto(
    int LineId,
    int DocumentId,
    string DocumentNumber,
    DateTime MovementDate,
    string? DocTypeCode,
    string? DocTypeName,
    byte MovementType,          // 1=Çıkış 2=Giriş 3=Transfer 4=Düzeltme
    string MovementLabel,
    decimal Quantity,           // ham (her zaman pozitif)
    decimal SignedQuantity,     // itemin toplam bakiyesine katkı (+/−; transfer=0)
    decimal RunningBalance,     // o ana kadarki koşan toplam bakiye (tüm geçmiş üzerinden)
    string? UnitCode,
    int? FromLocationId,
    string? FromLocationCode,
    string? FromLocationName,
    int? ToLocationId,
    string? ToLocationCode,
    string? ToLocationName,
    decimal? UnitCost,
    string? LotNo,
    string? CombinationCode,
    string? Notes,
    string? CreatedByName);

/// <summary>Stok hareketleri filtresi. Tüm alanlar opsiyonel — null ise o kriter uygulanmaz.</summary>
public sealed record ItemStockMovementFilter(
    int ItemId,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    byte? MovementType = null,
    int? LocationId = null);

/// <summary>Filtre dropdown'u için bu malzemenin hareketlerinde geçen lokasyon.</summary>
public sealed record StockMovementLocationDto(int Id, string Label);

/// <summary>
/// Stok hareketleri sonucu: gösterilecek satırlar (yeni→eski) + filtre lokasyonları + özet.
/// CurrentBalance tüm geçmiş üzerinden gerçek güncel bakiyedir; TotalIn/TotalOut yalnızca
/// filtrelenmiş (gösterilen) satırların toplamıdır.
/// </summary>
public sealed record ItemStockMovementResultDto(
    IReadOnlyList<ItemStockMovementRowDto> Rows,
    IReadOnlyList<StockMovementLocationDto> Locations,
    decimal TotalIn,
    decimal TotalOut,
    decimal CurrentBalance,
    int MovementCount);
