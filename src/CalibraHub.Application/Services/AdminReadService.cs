using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;

namespace CalibraHub.Application.Services;

public sealed class AdminReadService : IAdminReadService
{
    private readonly IIntegratorSettingsRepository _integratorSettingsRepository;
    private readonly ISmtpProfileRepository _smtpProfileRepository;
    private readonly IErpConnectionSettingsRepository _erpConnectionSettingsRepository;
    private readonly ICompanyRepository _companyDefinitionRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly IIntegratorImportLogRepository _integratorImportLogRepository;

    public AdminReadService(
        IIntegratorSettingsRepository integratorSettingsRepository,
        ISmtpProfileRepository smtpProfileRepository,
        IErpConnectionSettingsRepository erpConnectionSettingsRepository,
        ICompanyRepository companyDefinitionRepository,
        IDepartmentRepository departmentRepository,
        IUserProfileRepository userProfileRepository,
        IIntegratorImportLogRepository integratorImportLogRepository)
    {
        _integratorSettingsRepository = integratorSettingsRepository;
        _smtpProfileRepository = smtpProfileRepository;
        _erpConnectionSettingsRepository = erpConnectionSettingsRepository;
        _companyDefinitionRepository = companyDefinitionRepository;
        _departmentRepository = departmentRepository;
        _userProfileRepository = userProfileRepository;
        _integratorImportLogRepository = integratorImportLogRepository;
    }

    public async Task<AdminSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var companies = await _companyDefinitionRepository.GetAllAsync(cancellationToken);
        var integrators = await _integratorSettingsRepository.GetAllAsync(cancellationToken);
        var smtpProfiles = await _smtpProfileRepository.GetAllAsync(cancellationToken);
        var erpConnections = await _erpConnectionSettingsRepository.GetAllAsync(cancellationToken);
        var departments = await _departmentRepository.GetAllAsync(cancellationToken);
        var users = await _userProfileRepository.GetAllAsync(cancellationToken);

        var companyLookup = companies.ToDictionary(x => x.Id, x => x.Name);
        var departmentLookup = departments.ToDictionary(x => x.Id, x => x.Name);
        var userLookup = users.ToDictionary(x => x.Id, x => x.FullName);

        return new AdminSnapshotDto(
            Companies: companies
                .Select(x => new CompanyDto(
                    x.Id,
                    x.Name,
                    x.Title,
                    x.Address,
                    x.City,
                    x.District,
                    x.PostalCode,
                    x.TaxOffice,
                    x.TaxNumber,
                    x.IsEDocumentApprovalEnabled,
                    x.IsActive,
                    x.DatabaseConnectionString))
                .ToArray(),
            Integrators: integrators
                .Select(x => new IntegratorSettingsDto(
                    x.Id,
                    x.CompanyId,
                    companyLookup.GetValueOrDefault(x.CompanyId, "Tanimsiz"),
                    x.Name,
                    x.Provider.ToString(),
                    x.BaseUrl,
                    x.CompanyTaxNumber,
                    x.IsActive,
                    x.ScheduleEnabled,
                    x.PollingIntervalSeconds,
                    x.MaxRecordsPerPull,
                    x.LogRetentionDays,
                    x.IncludeReceivedDocumentsInPull,
                    x.MarkDownloadedDocumentsAsReceived,
                    x.IncludeIssuedEInvoicesInPull,
                    x.IncludeIssuedEArchivesInPull,
                    x.IncludeIssuedEDispatchesInPull,
                    x.Username,
                    x.Secret,
                    x.AppStr,
                    x.Source,
                    x.AppVersion,
                    x.TimeoutSeconds,
                    x.LookbackDays))
                .ToArray(),
            SmtpProfiles: smtpProfiles
                .Select(x => new SmtpProfileDto(
                    x.Id,
                    x.CompanyId,
                    companyLookup.GetValueOrDefault(x.CompanyId, "Tanimsiz"),
                    x.Name,
                    x.FromEmail,
                    x.FromDisplayName,
                    x.Host,
                    x.Port,
                    x.Username,
                    x.Password,
                    x.AuthMethod,
                    x.OAuth2ClientId,
                    x.OAuth2ClientSecret,
                    x.OAuth2RefreshToken,
                    x.UseSsl,
                    x.IsActive))
                .ToArray(),
            ErpConnections: erpConnections
                .Select(x => new ErpConnectionSettingsDto(
                    x.Id,
                    x.CompanyId,
                    companyLookup.GetValueOrDefault(x.CompanyId, "Tanimsiz"),
                    x.Provider,
                    x.Company,
                    x.Business,
                    x.Branch,
                    x.Username,
                    x.Password,
                    x.IsActive))
                .ToArray(),
            Departments: departments
                .Select(x => new DepartmentDto(
                    x.Id,
                    x.CompanyId,
                    companyLookup.GetValueOrDefault(x.CompanyId, "Tanimsiz"),
                    x.Code,
                    x.Name,
                    x.IsActive))
                .ToArray(),
            Users: users
                .Select(x => new UserProfileDto(
                    x.Id,
                    x.CompanyId,
                    companyLookup.GetValueOrDefault(x.CompanyId, "Tanimsiz"),
                    x.FullName,
                    x.Email,
                    x.EmployeeCode,
                    departmentLookup.GetValueOrDefault(x.DepartmentId, "Tanimsiz"),
                    x.SupervisorUserId.HasValue ? userLookup.GetValueOrDefault(x.SupervisorUserId.Value) : null,
                    UserAuthorizationCatalog.GetRoleLabel(x.Role),
                    x.Permissions.Select(UserAuthorizationCatalog.GetPermissionLabel).ToArray(),
                    x.IsActive))
                .ToArray());
    }

    public Task<IReadOnlyCollection<IntegratorImportLogEntryDto>> GetRecentIntegratorImportLogsAsync(
        int take,
        CancellationToken cancellationToken) =>
        _integratorImportLogRepository.GetRecentAsync(take, cancellationToken);
}
