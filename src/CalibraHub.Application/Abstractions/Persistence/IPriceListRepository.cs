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
    Task<IReadOnlyCollection<PriceListEntry>> GetEntriesByGroupAsync(int groupId, CancellationToken ct);
    Task<PriceListEntry?> GetEntryByIdAsync(int id, CancellationToken ct);
    Task<int> AddEntryAsync(PriceListEntry entry, CancellationToken ct);
    Task AddBulkEntriesAsync(IReadOnlyCollection<PriceListEntry> entries, CancellationToken ct);
    Task UpdateEntryAsync(PriceListEntry entry, CancellationToken ct);
    Task DeleteEntryAsync(int id, CancellationToken ct);
}
