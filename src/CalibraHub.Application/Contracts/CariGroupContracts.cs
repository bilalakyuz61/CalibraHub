namespace CalibraHub.Application.Contracts;

public sealed record CariGroupDto(int Id, string Code, string Name, int SortOrder, bool IsActive);

public sealed record CreateCariGroupRequest(string Name, int SortOrder = 0, bool IsActive = true);

public sealed record UpdateCariGroupRequest(int Id, string Name, int SortOrder, bool IsActive);
