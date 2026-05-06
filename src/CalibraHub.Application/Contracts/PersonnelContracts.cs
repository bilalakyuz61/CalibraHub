namespace CalibraHub.Application.Contracts;

public sealed record PersonnelDto(
    int Id,
    int CompanyId,
    string Code,
    string FullName,
    string? Title,
    string? Department,
    string? PinCode,
    string? CardNo,
    bool IsProductionOperator,
    bool IsActive,
    Guid? UserId,
    string? UserFullName,
    string? Phone,
    string? Email,
    string? Notes,
    DateTime Created,
    DateTime? Updated);

public sealed record SavePersonnelRequest(
    int Id,
    string Code,
    string FullName,
    string? Title,
    string? Department,
    string? PinCode,
    string? CardNo,
    bool IsProductionOperator,
    bool IsActive,
    Guid? UserId,
    string? Phone,
    string? Email,
    string? Notes);
