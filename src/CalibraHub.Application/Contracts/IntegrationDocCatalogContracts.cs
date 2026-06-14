namespace CalibraHub.Application.Contracts;

/// <summary>
/// Wizard Step 2'de "i" badge popover'inin gostergesi icin runtime field doc DTO'su.
/// Service tarafindan cache'lenmis halde doner.
/// </summary>
public sealed record IntegrationFieldDocRuntimeDto(
    string FieldPath,
    string? Label,
    string? Description,
    string? Example,
    string? Notes,
    bool   IsRequired,
    IntegrationEnumRuntimeDto? Enum);

public sealed record IntegrationEnumRuntimeDto(
    string Code,
    string Label,
    string? Description,
    IReadOnlyList<IntegrationEnumValueRuntimeDto> Values);

public sealed record IntegrationEnumValueRuntimeDto(
    string Value,
    string Label,
    string? TechnicalCode,
    string? Description);

// ── Admin DTOs ────────────────────────────────────────────────────────────────

public sealed record IntegrationProviderAdminDto(
    int     Id,
    string  Code,
    string  Label,
    string? Description,
    string? SourceInfo,
    string? IconColor,
    int     SortOrder,
    bool    IsActive,
    int     EnumCount,
    int     FieldDocCount);

public sealed record SaveIntegrationProviderRequest(
    int?    Id,
    string  Code,
    string  Label,
    string? Description,
    string? SourceInfo,
    string? IconColor,
    int     SortOrder,
    bool    IsActive);

public sealed record IntegrationEnumDefinitionAdminDto(
    int     Id,
    int     ProviderId,
    string  ProviderCode,
    string  Code,
    string  Label,
    string? Description,
    string? SourceInfo,
    bool    IsActive,
    IReadOnlyList<IntegrationEnumValueDto> Values,
    IReadOnlyList<IntegrationEnumFieldUsageDto> UsedInFields);  // YENI (2026-05-20): her usage endpointId + path tasir

public sealed record IntegrationEnumValueDto(
    int     Id,
    string  Value,
    string  Label,
    string? TechnicalCode,
    string? Description,
    int     SortOrder);

/// <summary>
/// Bir enum'un hangi endpoint'in hangi field path'inde kullanildigi.
/// EndpointId null ise "tum endpointler icin gecerli" (sadece path bazli match).
/// Backward compat: eski UsedInFieldPaths (string[]) formati EndpointId=null ile yorumlanir.
/// </summary>
public sealed record IntegrationEnumFieldUsageDto(
    int?    EndpointId,
    string  Path);

public sealed record SaveIntegrationEnumDefinitionRequest(
    int?    Id,
    int     ProviderId,
    string  Code,
    string  Label,
    string? Description,
    string? SourceInfo,
    bool    IsActive,
    IReadOnlyList<SaveIntegrationEnumValueRequest> Values,
    IReadOnlyList<IntegrationEnumFieldUsageDto>? UsedInFields = null);  // YENI (2026-05-20)

public sealed record SaveIntegrationEnumValueRequest(
    string  Value,
    string  Label,
    string? TechnicalCode,
    string? Description,
    int     SortOrder);

public sealed record IntegrationFieldDocAdminDto(
    int     Id,
    int     ProviderId,
    string  ProviderCode,
    string  Resource,
    string  FieldPath,
    string? Label,
    string? Description,
    string? Example,
    string? Notes,
    int?    EnumDefinitionId,
    string? EnumCode,
    bool    IsRequired,
    int     SortOrder,
    bool    IsActive);

public sealed record SaveIntegrationFieldDocRequest(
    int?    Id,
    int     ProviderId,
    string  Resource,
    string  FieldPath,
    string? Label,
    string? Description,
    string? Example,
    string? Notes,
    int?    EnumDefinitionId,
    bool    IsRequired,
    int     SortOrder,
    bool    IsActive);
