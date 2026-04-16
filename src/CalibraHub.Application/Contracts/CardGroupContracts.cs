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

// ── Card-group mappings (per entity: StockCard or ContactAccount) ──
// entityType: 1 = StockCard (GUID), 2 = ContactAccount (int)
public sealed record CardGroupMappingDto(int Level, int CardGroupId, string Code, string? Description);

public sealed record SaveCardGroupMappingsRequest(
    int EntityType,
    string EntityId,
    IReadOnlyCollection<CardGroupMappingLevelItem> Levels);

public sealed record CardGroupMappingLevelItem(int Level, int? CardGroupId);
