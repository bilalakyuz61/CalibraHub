namespace CalibraHub.Domain.Enums;

public enum UserPermission
{
    ManageIntegratorSettings = 1,
    ManageDepartments = 2,
    ManageUsers = 3,
    ViewIncomingDocuments = 4,
    ApproveDocuments = 5,
    RejectDocuments = 6,
    ExportReports = 7,
    ViewAuditLogs = 8,
    ManageCompanySettings = 9,
    ViewReports = 10,
    DesignReports = 11,
    ManageReports = 12,
    ViewDashboards = 13,
    DesignDashboards = 14,

    // Faz 1: Uretim Is Emri
    ManageWorkOrders = 15,
    ReleaseWorkOrders = 16,

    // Faz 3a: Üretim sahası operatörü
    ReportProduction = 17,
    OperateMachine = 18,

    // AR-GE modulu
    ManageArgeProjects = 19,
}
