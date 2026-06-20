using System.Collections.Concurrent;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Persistence.Repositories;

public sealed class InMemoryDataStore
{
    private const int DefaultCompanyId = 1;
    private const int FinanceDepartmentId = 1;
    private const int OperationsDepartmentId = 2;
    private const int AccountingManagerId = 1;
    private const int AccountantId = 2;
    private const int OperationsLeadId = 3;
    private const int IntegratorId = 1;
    private static readonly Guid NetsisErpId = Guid.Parse("5f01964e-9071-4dbc-9f3b-48fb9eae4665");

    public ConcurrentDictionary<int, Company> Companies { get; } = new(
        new[]
        {
            new KeyValuePair<int, Company>(
                DefaultCompanyId,
                new Company
                {
                    Id = DefaultCompanyId,
                    Name = "Calibra Merkez",
                    Title = "Calibra Teknoloji A.S.",
                    Address = "Istanbul",
                    TaxOffice = "Beyoglu",
                    TaxNumber = "1234567890",
                    IsEDocumentApprovalEnabled = false
                })
        });

    public ConcurrentDictionary<int, IntegratorSettings> IntegratorSettings { get; } = new(
        new[]
        {
            new KeyValuePair<int, IntegratorSettings>(
                IntegratorId,
                new IntegratorSettings
                {
                    Id = IntegratorId,
                    CompanyId = DefaultCompanyId,
                    Provider = IntegratorProvider.Logo,
                    Name = "Varsayilan Entegrator",
                    BaseUrl = "https://integrator.example.local",
                    CompanyTaxNumber = "1234567890",
                    Username = "calibra-api-user",
                    Secret = "***"
                })
        });

    public ConcurrentDictionary<Guid, ErpConnectionSettings> ErpConnectionSettings { get; } = new(
        new[]
        {
            new KeyValuePair<Guid, ErpConnectionSettings>(
                NetsisErpId,
                new ErpConnectionSettings
                {
                    Id = NetsisErpId,
                    CompanyId = DefaultCompanyId,
                    Provider = "Netsis",
                    Company = "01",
                    Business = "MERKEZ",
                    Branch = "GENEL",
                    Username = "netsis_user",
                    Password = "***"
                })
        });

    public ConcurrentDictionary<int, Department> Departments { get; } = new(
        new[]
        {
            new KeyValuePair<int, Department>(
                FinanceDepartmentId,
                new Department
                {
                    Id = FinanceDepartmentId,
                    CompanyId = DefaultCompanyId,
                    Name = "Finans"
                }),
            new KeyValuePair<int, Department>(
                OperationsDepartmentId,
                new Department
                {
                    Id = OperationsDepartmentId,
                    CompanyId = DefaultCompanyId,
                    Name = "Operasyon"
                })
        });

    public ConcurrentDictionary<int, UserProfile> Users { get; } = new(
        new[]
        {
            new KeyValuePair<int, UserProfile>(
                AccountingManagerId,
                new UserProfile
                {
                    Id = AccountingManagerId,
                    CompanyId = DefaultCompanyId,
                    FullName = "Ayse Kaya",
                    Email = "ayse.kaya@calibra.local",
                    EmployeeCode = "EMP-001",
                    DepartmentId = FinanceDepartmentId,
                    Role = UserRole.SystemAdmin,
                    Permissions = new[]
                    {
                        UserPermission.ManageIntegratorSettings,
                        UserPermission.ManageCompanySettings,
                        UserPermission.ManageDepartments,
                        UserPermission.ManageUsers,
                        UserPermission.ViewIncomingDocuments,
                        UserPermission.ApproveDocuments,
                        UserPermission.RejectDocuments,
                        UserPermission.ExportReports,
                        UserPermission.ViewAuditLogs
                    }
                }),
            new KeyValuePair<int, UserProfile>(
                AccountantId,
                new UserProfile
                {
                    Id = AccountantId,
                    CompanyId = DefaultCompanyId,
                    FullName = "Mehmet Demir",
                    Email = "mehmet.demir@calibra.local",
                    EmployeeCode = "EMP-002",
                    DepartmentId = FinanceDepartmentId,
                    SupervisorUserId = AccountingManagerId,
                    Role = UserRole.Approver,
                    Permissions = new[]
                    {
                        UserPermission.ViewIncomingDocuments,
                        UserPermission.ApproveDocuments,
                        UserPermission.RejectDocuments
                    }
                }),
            new KeyValuePair<int, UserProfile>(
                OperationsLeadId,
                new UserProfile
                {
                    Id = OperationsLeadId,
                    CompanyId = DefaultCompanyId,
                    FullName = "Zeynep Arslan",
                    Email = "zeynep.arslan@calibra.local",
                    EmployeeCode = "EMP-003",
                    DepartmentId = OperationsDepartmentId,
                    Role = UserRole.DepartmentManager,
                    Permissions = new[]
                    {
                        UserPermission.ManageDepartments,
                        UserPermission.ViewIncomingDocuments,
                        UserPermission.ApproveDocuments,
                        UserPermission.RejectDocuments,
                        UserPermission.ExportReports
                    }
                })
        });

    public ConcurrentDictionary<Guid, IncomingDocument> IncomingDocuments { get; } = new(
        new[]
        {
            new KeyValuePair<Guid, IncomingDocument>(
                Guid.Parse("e4e49b33-2575-4407-8704-4dff117ac4d8"),
                new IncomingDocument
                {
                    Id = Guid.Parse("e4e49b33-2575-4407-8704-4dff117ac4d8"),
                    IntegratorSettingsId = IntegratorId,
                    EnvelopeId = "ENV-INIT-0001",
                    DocumentNumber = "FTR-2026-0001",
                    Kind = DocumentKind.EInvoice,
                    IssueDate = DateOnly.FromDateTime(DateTime.Now.Date),
                    SenderTaxNumber = "1111111111",
                    RecipientTaxNumber = "1234567890",
                    PayloadRaw = "{}"
                })
        });

    public ConcurrentDictionary<int, SmtpProfile> SmtpProfiles { get; } = new();

    public ConcurrentDictionary<Guid, UiLabelTranslation> UiLabelTranslations { get; } = new();

    public ConcurrentDictionary<string, ScreenLayoutDefinition> ScreenLayouts { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentDictionary<Guid, IntegratorImportLogEntryDto> IntegratorImportLogs { get; } = new();
}
