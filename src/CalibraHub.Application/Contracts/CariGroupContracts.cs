namespace CalibraHub.Application.Contracts;

public sealed record CariGroupDto(int Id, string Code, string Name, int SortOrder, bool IsActive, int GroupCategory = 1);

public sealed record CreateCariGroupRequest(string Name, int SortOrder = 0, bool IsActive = true, int GroupCategory = 1);

public sealed record UpdateCariGroupRequest(int Id, string Name, int SortOrder, bool IsActive, int GroupCategory = 1);

public sealed record ContactGroupMappingDto(int SlotOrder, string GroupCode, string? GroupDescription);
