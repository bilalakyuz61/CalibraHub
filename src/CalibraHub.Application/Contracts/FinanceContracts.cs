namespace CalibraHub.Application.Contracts;

public sealed record ContactAccountDto(
    int Id,
    byte AccountType,
    string AccountCode,
    string AccountTitle,
    string? TaxNumber,
    string? IdentityNumber,
    string? TaxOffice,
    string? Phone,
    string? Email,
    string? Address,
    string? City,
    bool IsActive,
    int? PriceGroupId,
    DateTime CreatedAt);

public sealed record SaveContactAccountRequest(
    int? Id,
    byte AccountType,
    string AccountCode,
    string AccountTitle,
    string? TaxNumber,
    string? IdentityNumber,
    string? TaxOffice,
    string? Phone,
    string? Email,
    string? Address,
    string? City,
    bool IsActive,
    int? PriceGroupId);

public sealed record DeleteContactAccountBody(int Id);
