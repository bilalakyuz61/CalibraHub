namespace CalibraHub.Application.Contracts;

public sealed record IntegrationApiProfileDto(
    Guid Id, int CompanyId, string Name, string AuthType, string BaseUrl,
    string? AuthConfigJson, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt,
    string? ProviderCode);

public sealed record SaveIntegrationApiProfileRequest(
    Guid? Id, string Name, string AuthType, string BaseUrl, string? AuthConfigJson, bool IsActive,
    string? ProviderCode);
