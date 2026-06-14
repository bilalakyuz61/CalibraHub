using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

public sealed record SaveCompanyRequest(
    int? Id,
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
    string? DatabaseConnectionString);

public sealed record SaveIntegratorSettingsRequest(
    int? Id,
    int CompanyId,
    IntegratorProvider Provider,
    string Name,
    string BaseUrl,
    string CompanyTaxNumber,
    string Username,
    string Secret,
    int PollingIntervalSeconds,
    int MaxRecordsPerPull,
    int LogRetentionDays,
    bool IncludeReceivedDocumentsInPull,
    bool MarkDownloadedDocumentsAsReceived,
    bool IncludeIssuedEInvoicesInPull,
    bool IncludeIssuedEArchivesInPull,
    bool IncludeIssuedEDispatchesInPull,
    bool IsActive,
    bool ScheduleEnabled,
    string? AppStr,
    string? Source,
    string? AppVersion,
    int TimeoutSeconds = 30,
    int LookbackDays = 30);

public sealed record TestIntegratorConnectionRequest(
    int CompanyId,
    IntegratorProvider Provider,
    string Name,
    string BaseUrl,
    string CompanyTaxNumber,
    string Username,
    string Secret,
    int PollingIntervalSeconds,
    int MaxRecordsPerPull,
    int LogRetentionDays,
    bool IncludeReceivedDocumentsInPull,
    bool MarkDownloadedDocumentsAsReceived,
    bool IncludeIssuedEInvoicesInPull,
    bool IncludeIssuedEArchivesInPull,
    bool IncludeIssuedEDispatchesInPull,
    string? AppStr,
    string? Source,
    string? AppVersion,
    int TimeoutSeconds = 30,
    int LookbackDays = 30);

public sealed record IntegratorConnectionTestResult(bool IsSuccess, string Message);

public sealed record SaveSmtpProfileRequest(
    Guid? Id,
    int CompanyId,
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

public sealed record SaveErpConnectionSettingsRequest(
    Guid? Id,
    int CompanyId,
    string Company,
    string Business,
    string Branch,
    string Username,
    string Password,
    bool IsActive);

public sealed record TestErpConnectionRequest(
    int CompanyId,
    string Company,
    string Business,
    string Branch,
    string Username,
    string Password);

public sealed record ErpConnectionTestResult(bool IsSuccess, string Message);

public sealed record TestSmtpConnectionRequest(
    int CompanyId,
    string Name,
    string FromEmail,
    string FromDisplayName,
    string Host,
    int Port,
    string Username,
    string Password,
    bool UseSsl,
    string? TestRecipientEmail);

public sealed record SmtpConnectionTestResult(bool IsSuccess, string Message);

public sealed record CreateDepartmentRequest(int CompanyId, string Name);

public sealed record UpdateDepartmentRequest(int Id, string Name, bool IsActive);

public sealed record CreateUserRequest(
    int CompanyId,
    string FullName,
    string Email,
    string EmployeeCode,
    int? DepartmentId,
    int? SupervisorUserId,
    UserRole Role,
    IReadOnlyCollection<UserPermission> Permissions,
    string? Password = null,
    GrafanaRole? GrafanaRole = null);

public sealed record UpdateUserRequest(
    int Id,
    int CompanyId,
    string FullName,
    string Email,
    string? Password = null,
    // GrafanaRole degeri:
    //   • SetGrafanaRole = false → mevcut rol korunur (geriye uyumlu — SetupController gibi caller'lar)
    //   • SetGrafanaRole = true && GrafanaRole = null → kullanici Grafana'dan cikarilir
    //   • SetGrafanaRole = true && GrafanaRole != null → kullanici Grafana'ya eklenir/rolu guncellenir
    bool SetGrafanaRole = false,
    GrafanaRole? GrafanaRole = null,
    // Role / Permissions:
    //   • SetRole = false → mevcut rol/izinler korunur (geriye uyumlu)
    //   • SetRole = true && Role != null → rol guncellenir; izinler de UserAuthorizationCatalog'tan turetilip set edilir
    bool SetRole = false,
    UserRole? Role = null);
