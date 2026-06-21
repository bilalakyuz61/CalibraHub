namespace CalibraHub.Application.Contracts;

public sealed record AdminSnapshotDto(
    IReadOnlyCollection<CompanyDto> Companies,
    IReadOnlyCollection<IntegratorSettingsDto> Integrators,
    IReadOnlyCollection<SmtpProfileDto> SmtpProfiles,
    IReadOnlyCollection<ErpConnectionSettingsDto> ErpConnections,
    IReadOnlyCollection<DepartmentDto> Departments,
    IReadOnlyCollection<UserProfileDto> Users);

public sealed record CompanyDto(
    int Id,
    string Name,
    string Title,
    string Address,
    string? City,
    string? District,
    string? PostalCode,
    string TaxOffice,
    string TaxNumber,
    bool IsEDocumentApprovalEnabled,
    bool IsActive,
    string? DatabaseConnectionString,
    string? PublicBaseUrl);

public sealed record IntegratorSettingsDto(
    int Id,
    int CompanyId,
    string CompanyName,
    string Name,
    string Provider,
    string BaseUrl,
    string CompanyTaxNumber,
    bool IsActive,
    bool ScheduleEnabled,
    int PollingIntervalSeconds,
    int MaxRecordsPerPull,
    int LogRetentionDays,
    bool IncludeReceivedDocumentsInPull,
    bool MarkDownloadedDocumentsAsReceived,
    bool IncludeIssuedEInvoicesInPull,
    bool IncludeIssuedEArchivesInPull,
    bool IncludeIssuedEDispatchesInPull,
    string Username,
    string Secret,
    string? AppStr,
    string? Source,
    string? AppVersion,
    int TimeoutSeconds,
    int LookbackDays);

public sealed record SmtpProfileDto(
    int Id,
    int CompanyId,
    string CompanyName,
    string Name,
    string FromEmail,
    string FromDisplayName,
    string Host,
    int Port,
    string Username,
    string Password,
    string AuthMethod,
    string? OAuth2ClientId,
    string? OAuth2ClientSecret,
    string? OAuth2RefreshToken,
    bool UseSsl,
    bool IsActive);

public sealed record ErpConnectionSettingsDto(
    Guid Id,
    int CompanyId,
    string CompanyName,
    string Provider,
    string Company,
    string Business,
    string Branch,
    string Username,
    string Password,
    bool IsActive);

public sealed record DepartmentDto(
    int Id,
    int CompanyId,
    string CompanyName,
    string Name,
    bool IsActive);

public sealed record UserProfileDto(
    int Id,
    int CompanyId,
    string CompanyName,
    string FullName,
    string Email,
    string EmployeeCode,
    string DepartmentName,
    string? SupervisorName,
    string Role,
    IReadOnlyCollection<string> Permissions,
    bool IsActive);
