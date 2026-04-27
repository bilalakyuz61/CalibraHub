namespace CalibraHub.Application.Contracts;

// ── Fiyat Grubu ──────────────────────────────────────────────────────────────
public sealed record PriceGroupDto(
    int Id, string GroupCode, string GroupName, string? Description, bool IsActive,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreatePriceGroupRequest(string GroupCode, string GroupName, string? Description, bool IsActive);
public sealed record UpdatePriceGroupRequest(int Id, string GroupCode, string GroupName, string? Description, bool IsActive);

// ── Fiyat Kalemi ─────────────────────────────────────────────────────────────
public sealed record PriceListDto(
    int Id, int PriceGroupId, int? ItemId,
    string MaterialCode, string? MaterialName,
    string? CombinationCode, string? CombinationName,
    string Currency, decimal BuyingPrice, decimal SellingPrice,
    DateTime ValidFrom, DateTime? ValidTo, bool IsActive,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed record SavePriceListRequest(
    int? Id, int PriceGroupId,
    int? ItemId, string MaterialCode, string? MaterialName,
    string? CombinationCode, string? CombinationName,
    string Currency, decimal BuyingPrice, decimal SellingPrice,
    DateTime ValidFrom, DateTime? ValidTo, bool IsActive);

// Inline fiyat guncelleme (sadece alis/satis fiyati degistirir, diger alanlar korunur)
public sealed record UpdatePriceEntryRequest(int Id, decimal BuyingPrice, decimal SellingPrice);

// ── Toplu Fiyat Girisi ──────────────────────────────────────────────────────
public sealed record BulkPriceEntryLine(
    int? ItemId, string MaterialCode, string? MaterialName,
    string? CombinationCode, string? CombinationName,
    decimal BuyingPrice, decimal SellingPrice);

public sealed record SaveBulkPriceEntriesRequest(
    int PriceGroupId, string Currency,
    DateTime ValidFrom, DateTime? ValidTo,
    IReadOnlyCollection<BulkPriceEntryLine> Lines);

// ── Mevcut Fiyat Sorgusu ────────────────────────────────────────────────────
public sealed record PriceEntryKey(int? ItemId, string MaterialCode, string? CombinationCode);

public sealed record GetExistingPricesRequest(
    int PriceGroupId, string Currency, DateTime ValidFrom,
    IReadOnlyCollection<PriceEntryKey> Keys);

public sealed record ExistingPriceRow(
    int? ItemId, string MaterialCode, string? CombinationCode,
    decimal BuyingPrice, decimal SellingPrice);

// ── Upsert Sonucu ───────────────────────────────────────────────────────────
public sealed record BulkUpsertResult(int Inserted, int Updated);
