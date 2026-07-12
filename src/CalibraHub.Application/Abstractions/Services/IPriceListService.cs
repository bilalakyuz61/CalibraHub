using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IPriceListService
{
    // Fiyat Gruplari
    Task<IReadOnlyCollection<PriceGroupDto>> GetAllGroupsAsync(CancellationToken ct);
    Task<PriceGroupDto?> GetGroupByIdAsync(int id, CancellationToken ct);
    Task<(bool Success, string? Error, int? Id)> CreateGroupAsync(CreatePriceGroupRequest request, CancellationToken ct);
    Task<(bool Success, string? Error)> UpdateGroupAsync(UpdatePriceGroupRequest request, CancellationToken ct);
    Task<(bool Success, string? Error)> DeleteGroupAsync(int id, CancellationToken ct);

    // "Genel Liste" (fallback) grubunu isaretle — CompanyId basina tek default.
    Task<(bool Success, string? Error)> SetDefaultGroupAsync(int groupId, CancellationToken ct);

    // Fiyat Kalemleri — server-side pagination + filter
    Task<PagedPriceListResult> GetEntriesByGroupAsync(
        int groupId, PriceListFilter filter, CancellationToken ct);
    Task<(bool Success, string? Error, int? Id)> SaveEntryAsync(SavePriceListRequest request, CancellationToken ct);
    Task<(bool Success, string? Error)> DeleteEntryAsync(int id, CancellationToken ct);

    // Inline fiyat guncelleme (sadece alis/satis degistirir)
    Task<(bool Success, string? Error)> UpdateEntryPricesAsync(UpdatePriceEntryRequest request, CancellationToken ct);

    // Toplu Fiyat Girisi (upsert — mevcut kayit varsa guncelle, yoksa ekle)
    Task<(bool Success, string? Error, int Inserted, int Updated)> SaveBulkEntriesAsync(SaveBulkPriceEntriesRequest request, CancellationToken ct);

    // Mevcut fiyat sorgusu — toplu stok+kombinasyon listesi icin
    Task<IReadOnlyCollection<ExistingPriceRow>> GetExistingPricesAsync(GetExistingPricesRequest request, CancellationToken ct);

    // Belge/kit satirina fallback'li fiyat: cari listesi (varsa) → Genel Liste.
    Task<IReadOnlyCollection<ResolvedPriceRow>> ResolveLinePricesAsync(ResolveLinePricesRequest request, CancellationToken ct);
}
