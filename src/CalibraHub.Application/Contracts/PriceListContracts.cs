namespace CalibraHub.Application.Contracts;

// ── Fiyat Grubu ──────────────────────────────────────────────────────────────
public sealed record PriceGroupDto(
    int Id, string GroupCode, string GroupName, string? Description, bool IsActive,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreatePriceGroupRequest(string GroupCode, string GroupName, string? Description, bool IsActive);
public sealed record UpdatePriceGroupRequest(int Id, string GroupCode, string GroupName, string? Description, bool IsActive);

// ── Fiyat Kalemi ─────────────────────────────────────────────────────────────
public sealed record PriceListEntryDto(
    int Id, int PriceGroupId, int? StockCardId,
    string MaterialCode, string? MaterialName,
    string Currency, decimal BuyingPrice, decimal SellingPrice,
    DateTime ValidFrom, DateTime? ValidTo, bool IsActive,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed record SavePriceListEntryRequest(
    int? Id, int PriceGroupId,
    int? StockCardId, string MaterialCode, string? MaterialName,
    string Currency, decimal BuyingPrice, decimal SellingPrice,
    DateTime ValidFrom, DateTime? ValidTo, bool IsActive);

// Inline fiyat guncelleme (sadece alis/satis fiyati degistirir, diger alanlar korunur)
public sealed record UpdatePriceEntryRequest(int Id, decimal BuyingPrice, decimal SellingPrice);

// ── Toplu Fiyat Girisi ──────────────────────────────────────────────────────
public sealed record BulkPriceEntryLine(
    int? StockCardId, string MaterialCode, string? MaterialName,
    decimal BuyingPrice, decimal SellingPrice);

public sealed record SaveBulkPriceEntriesRequest(
    int PriceGroupId, string Currency,
    DateTime ValidFrom, DateTime? ValidTo,
    IReadOnlyCollection<BulkPriceEntryLine> Lines);
