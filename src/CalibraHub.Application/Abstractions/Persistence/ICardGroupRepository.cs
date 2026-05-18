using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ICardGroupRepository
{
    Task<IReadOnlyCollection<CardGroup>> GetByLevelAsync(int cardType, int level, int? parentId, CancellationToken ct);
    Task<IReadOnlyCollection<CardGroup>> GetByParentAsync(int parentId, CancellationToken ct);
    Task<CardGroup?> GetByIdAsync(int id, CancellationToken ct);
    Task<bool> HasChildrenAsync(int id, CancellationToken ct);
    Task<int> AddAsync(CardGroup group, CancellationToken ct);
    Task UpdateAsync(CardGroup group, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);

    // ── Entity group mappings ──
    Task<IReadOnlyCollection<CardGroupMappingRow>> GetEntityMappingsAsync(int entityType, string entityId, CancellationToken ct);
    Task SaveEntityMappingsAsync(int entityType, string entityId, IReadOnlyCollection<(int Level, int? CardGroupId)> levels, CancellationToken ct);
}

public sealed record CardGroupMappingRow(int Level, int CardGroupId, string Code, string? Description);
