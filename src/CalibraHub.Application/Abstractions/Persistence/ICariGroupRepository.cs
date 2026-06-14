using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ICariGroupRepository
{
    Task<IReadOnlyCollection<CariGroup>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CariGroup>> GetAllAsync(int? category, CancellationToken cancellationToken);
    Task<CariGroup?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<int> AddAsync(CariGroup entity, CancellationToken cancellationToken);
    Task UpdateAsync(CariGroup entity, CancellationToken cancellationToken);
    Task DeleteAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<int, IReadOnlyList<ContactGroupMappingDto>>> GetContactGroupMappingsBatchAsync(
        IReadOnlyCollection<int> contactIds, CancellationToken cancellationToken);

    Task SaveContactGroupMappingsAsync(int contactId, IReadOnlyCollection<(int Slot, string Code)> mappings, CancellationToken cancellationToken);
}
