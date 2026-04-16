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

    // Fiyat Kalemleri
    Task<IReadOnlyCollection<PriceListEntryDto>> GetEntriesByGroupAsync(int groupId, CancellationToken ct);
    Task<(bool Success, string? Error, int? Id)> SaveEntryAsync(SavePriceListEntryRequest request, CancellationToken ct);
    Task<(bool Success, string? Error)> DeleteEntryAsync(int id, CancellationToken ct);

    // Inline fiyat guncelleme (sadece alis/satis degistirir)
    Task<(bool Success, string? Error)> UpdateEntryPricesAsync(UpdatePriceEntryRequest request, CancellationToken ct);

    // Toplu Fiyat Girisi
    Task<(bool Success, string? Error, int Count)> SaveBulkEntriesAsync(SaveBulkPriceEntriesRequest request, CancellationToken ct);
}
