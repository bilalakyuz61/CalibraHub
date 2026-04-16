namespace CalibraHub.Application.Contracts;

public sealed record AuthenticatedUserDto(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    int CompanyId,
    string CompanyName);
