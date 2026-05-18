using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IPriceListRepository
{
    // Fiyat Gruplari
    Task<IReadOnlyCollection<PriceGroup>> GetAllGroupsAsync(CancellationToken ct);
    Task<PriceGroup?> GetGroupByIdAsync(int id, CancellationToken ct);
    Task<int> AddGroupAsync(PriceGroup group, CancellationToken ct);
    Task UpdateGroupAsync(PriceGroup group, CancellationToken ct);
    Task DeleteGroupAsync(int id, CancellationToken ct);

    // Fiyat Kalemleri — enriched (Item/Config/Currency JOIN ile)
    // Server-side pagination + filter (frontend tum kayitlari cekmiyor; bellek korumasi).
    Task<PagedPriceListResult> GetEntriesByGroupAsync(
        int groupId, PriceListFilter filter, CancellationToken ct);
    Task<PriceList?> GetEntryByIdAsync(int id, CancellationToken ct);
    Task<int> AddEntryAsync(PriceList entry, CancellationToken ct);
    Task AddBulkEntriesAsync(IReadOnlyCollection<PriceList> entries, CancellationToken ct);
    Task UpdateEntryAsync(PriceList entry, CancellationToken ct);
    Task DeleteEntryAsync(int id, CancellationToken ct);

    /// <summary>
    /// Aynı (Group + Item + Config + Currency + PriceType + ValidFrom) kombinasyonuna sahip
    /// AKTİF kayıt arar — varsa duplicate engellemek için kullanılır. excludeId kendisini hariç tutar.
    /// </summary>
    Task<PriceList?> FindActiveDuplicateAsync(
        int groupId, int itemId, int? configId, int currencyId,
        string priceType, DateTime validFrom,
        int excludeId, CancellationToken ct);

    // Upsert (bulk) — Ayni grup+item+config+tarih varsa guncelle, yoksa ekle
    Task<BulkUpsertResult> UpsertBulkEntriesAsync(
        IReadOnlyCollection<PriceList> entries, CancellationToken ct);

    // Mevcut fiyat sorgusu — secilen PriceType (Buying/Selling) icin toplu anahtarla
    Task<IReadOnlyCollection<ExistingPriceRow>> GetExistingPricesAsync(
        int groupId, int currencyId, string priceType, DateTime validFrom,
        IReadOnlyCollection<PriceEntryKey> keys, CancellationToken ct);
}
