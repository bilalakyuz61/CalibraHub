namespace CalibraHub.Application.Contracts;

public sealed record AuthenticatedUserDto(
    int Id,
    string FullName,
    string Email,
    string Role,
    int CompanyId,
    string CompanyName,
    int? DepartmentId = null);
