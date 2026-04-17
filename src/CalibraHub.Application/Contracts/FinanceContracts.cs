namespace CalibraHub.Application.Contracts;

public sealed record ContactDto(
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
    string? District,
    bool IsActive,
    int? PriceGroupId,
    DateTime CreatedAt);

public sealed record SaveContactRequest(
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
    string? District,
    bool IsActive,
    int? PriceGroupId);

public sealed record DeleteContactBody(int Id);
