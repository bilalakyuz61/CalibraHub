namespace CalibraHub.Application.Contracts;

// ── Fiyat Grubu ──────────────────────────────────────────────────────────────
// AllowsBuying / AllowsSelling / AllowsCost: bu gruba hangi fiyat tipi (b/s/m) girilebilir?
// En az bir tane true olmali (servis tarafinda dogrulanir).
public sealed record PriceGroupDto(
    int Id, string Code, string Name, string? Description, bool IsActive,
    bool AllowsBuying, bool AllowsSelling, bool AllowsCost,
    bool IsDefault,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreatePriceGroupRequest(
    string Code, string Name, string? Description, bool IsActive,
    bool AllowsBuying = true, bool AllowsSelling = true, bool AllowsCost = true);

public sealed record UpdatePriceGroupRequest(
    int Id, string Code, string Name, string? Description, bool IsActive,
    bool AllowsBuying = true, bool AllowsSelling = true, bool AllowsCost = true);

// ── Fiyat Kalemi (her DB satiri = tek fiyat: PriceType + Price) ───────────────
// UI'da her satir tek bir tip gosterir (Alis veya Satis); ayni urun icin ayri
// satirlarla hem alis hem satis tanimlanir.
public sealed record PriceListDto(
    int Id, int GroupId,
    int ItemId, string ItemCode, string ItemName,
    int? ConfigId, string? ConfigCode,
    int CurrencyId, string CurrencyCode,
    string PriceType, decimal Price,
    DateTime ValidFrom, DateTime? ValidTo, bool IsActive,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed record SavePriceListRequest(
    int? Id, int GroupId,
    int ItemId, int? ConfigId,
    int CurrencyId, string PriceType, decimal Price,
    DateTime ValidFrom, DateTime? ValidTo, bool IsActive);

// Inline fiyat guncelleme — kismi update. Yalniz Id zorunlu; gonderilmeyen
// (null) alanlar mevcut degerden korunur. Frontend tum 5 alani gonderebilir
// (Currency / PriceType / Price / ValidFrom / ValidTo) — tek tanimda hem
// "yalniz fiyat" hem "tum alanlar" akislari karsilanir.
public sealed record UpdatePriceEntryRequest(
    int Id,
    decimal? Price       = null,
    int? CurrencyId      = null,
    string? PriceType    = null,
    DateTime? ValidFrom  = null,
    DateTime? ValidTo    = null,
    bool ClearValidTo    = false);

// ── Toplu Fiyat Girisi ──────────────────────────────────────────────────────
public sealed record BulkPriceEntryLine(
    int ItemId, int? ConfigId, decimal Price);

public sealed record SaveBulkPriceEntriesRequest(
    int GroupId, int CurrencyId, string PriceType,
    DateTime ValidFrom, DateTime? ValidTo,
    IReadOnlyCollection<BulkPriceEntryLine> Lines);

// ── Mevcut Fiyat Sorgusu ────────────────────────────────────────────────────
public sealed record PriceEntryKey(int ItemId, int? ConfigId);

public sealed record GetExistingPricesRequest(
    int GroupId, int CurrencyId, string PriceType, DateTime ValidFrom,
    IReadOnlyCollection<PriceEntryKey> Keys);

public sealed record ExistingPriceRow(
    int ItemId, int? ConfigId, decimal Price);

// ── Fiyat Cozucu (belge/kit satirina fallback'li fiyat) ──────────────────────
// Yon → PriceType: Sales='s', Purchase='b', Cost='m'.
public enum PriceDirection { Sales, Purchase, Cost }

public sealed record ResolveLinePricesRequest(
    int? ContactId, int CurrencyId, PriceDirection Direction,
    DateTime Date, IReadOnlyCollection<PriceEntryKey> Keys);

// Fiyatin hangi listeden/eslesmeden geldigi (UI rozet/teshis; None = cozulemedi).
// Oncelik: ContactExact > ContactBase > DefaultExact > DefaultBase.
public enum PriceSource { None, ContactExact, ContactBase, DefaultExact, DefaultBase }

public sealed record ResolvedPriceRow(
    int ItemId, int? ConfigId, decimal? Price, int? SourceGroupId, PriceSource Source);

// ── Upsert Sonucu ───────────────────────────────────────────────────────────
public sealed record BulkUpsertResult(int Inserted, int Updated);

// ── Liste / Pagination Filtre ────────────────────────────────────────────────
public sealed record PriceListFilter(
    string? Search,
    int? CurrencyId,
    string? PriceType,
    DateTime? ValidFromMin,
    DateTime? ValidToMax,
    DateTime? ActiveOn,
    int Page,
    int PageSize);

public sealed record PagedPriceListResult(
    IReadOnlyCollection<PriceListDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
