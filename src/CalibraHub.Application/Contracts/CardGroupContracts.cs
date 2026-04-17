namespace CalibraHub.Application.Contracts;

public sealed record CardGroupDto(
    int Id,
    int CardType,
    int Level,
    int? ParentId,
    string? ParentCode,
    string Code,
    string? Description);

public sealed record SaveCardGroupRequest(
    int? Id,
    int CardType,
    int Level,
    int? ParentId,
    string Code,
    string? Description);

public sealed record DeleteCardGroupRequest(int Id);

// ── Card-group mappings (per entity: Item or Contact) ──
// entityType: 1 = Item (GUID), 2 = Contact (int)
public sealed record CardGroupMappingDto(int Level, int CardGroupId, string Code, string? Description);

public sealed record SaveCardGroupMappingsRequest(
    int EntityType,
    string EntityId,
    IReadOnlyCollection<CardGroupMappingLevelItem> Levels);

public sealed record CardGroupMappingLevelItem(int Level, int? CardGroupId);
