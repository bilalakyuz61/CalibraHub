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
    DateTime CreatedAt,
    string? CountryCode = null,
    string? Mobile = null,
    string? Website = null,
    string? PostalCode = null,
    string? ContactPerson = null,
    string? Neighborhood = null,
    int? SalesRepresentativeId = null,
    string? WaPhone = null,
    string? WaName = null,
    int? ContactGroupId = null);

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
    int? PriceGroupId,
    string? CountryCode = null,
    string? Mobile = null,
    string? Website = null,
    string? PostalCode = null,
    string? ContactPerson = null,
    string? Neighborhood = null,
    int? SalesRepresentativeId = null,
    string? WaPhone = null,
    string? WaName = null,
    int? ContactGroupId = null);

public sealed record DeleteContactBody(int Id);

/// <summary>Cariye bagli iletisim kisisi (firma calisani/temsilcisi) — okuma DTO'su.</summary>
public sealed record ContactPersonDto(
    int Id,
    int ContactId,
    string Title,
    string FullName,
    string? Phone,
    string? Email,
    string? Notes,
    bool IsPrimary,
    bool IsActive,
    DateTime Created,
    DateTime? Updated,
    int? TitleId = null,
    string? TitleName = null);

/// <summary>Upsert (Id=0 → insert, Id>0 → update) request DTO'su.</summary>
public sealed record SaveContactPersonRequest(
    int Id,
    int ContactId,
    string Title,
    string FullName,
    string? Phone,
    string? Email,
    string? Notes,
    bool IsPrimary,
    bool IsActive = true,
    int? TitleId = null);

/// <summary>ContactPerson icin onceden tanimli unvan lookup kaydi.</summary>
public sealed record ContactPersonTitleDto(
    int Id,
    string Name,
    int SortOrder,
    bool IsSystem,
    bool IsActive);

/// <summary>Yeni unvan ekleme (inline "+ Yeni") veya admin-side rename. Id=0 ise insert; >0 ise update (Phase 1 sadece inline insert kullanilir).</summary>
public sealed record SaveContactPersonTitleRequest(int Id, string Name);
