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

    // Fiyat Kalemleri
    Task<IReadOnlyCollection<PriceList>> GetEntriesByGroupAsync(int groupId, CancellationToken ct);
    Task<PriceList?> GetEntryByIdAsync(int id, CancellationToken ct);
    Task<int> AddEntryAsync(PriceList entry, CancellationToken ct);
    Task AddBulkEntriesAsync(IReadOnlyCollection<PriceList> entries, CancellationToken ct);
    Task UpdateEntryAsync(PriceList entry, CancellationToken ct);
    Task DeleteEntryAsync(int id, CancellationToken ct);

    // Upsert (bulk) — Ayni grup+stok+kombinasyon+tarih varsa guncelle, yoksa ekle
    Task<BulkUpsertResult> UpsertBulkEntriesAsync(
        IReadOnlyCollection<PriceList> entries, CancellationToken ct);

    // Mevcut fiyat sorgusu — toplu anahtarla
    Task<IReadOnlyCollection<ExistingPriceRow>> GetExistingPricesAsync(
        int priceGroupId, string currency, DateTime validFrom,
        IReadOnlyCollection<PriceEntryKey> keys, CancellationToken ct);
}
