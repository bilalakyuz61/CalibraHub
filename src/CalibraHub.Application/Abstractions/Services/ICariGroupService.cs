using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface ICariGroupService
{
    Task<IReadOnlyCollection<CariGroupDto>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CariGroupDto>> GetAllAsync(int? category, CancellationToken cancellationToken);
    Task<CariGroupDto?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<(bool Success, string? Error, int? Id)> CreateAsync(CreateCariGroupRequest request, CancellationToken cancellationToken);
    Task<(bool Success, string? Error)> UpdateAsync(UpdateCariGroupRequest request, CancellationToken cancellationToken);
    Task<(bool Success, string? Error)> DeleteAsync(int id, CancellationToken cancellationToken);

    /// <summary>Birden cok cari icin grup eslestirmelerini tek seferde getirir.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<ContactGroupMappingDto>>> GetContactGroupMappingsBatchAsync(
        IReadOnlyCollection<int> contactIds, CancellationToken cancellationToken);

    /// <summary>Cari icin 5 slot grup eslestirmesini tamamen yeniden yazar (full replace).</summary>
    Task SaveContactGroupMappingsAsync(int contactId, IReadOnlyCollection<(int Slot, string Code)> mappings, CancellationToken cancellationToken);
}
