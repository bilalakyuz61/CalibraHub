using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Security;

public static class UserAuthorizationCatalog
{
    private static readonly IReadOnlyDictionary<UserRole, IReadOnlyCollection<UserPermission>> AllowedPermissionsByRole =
        new Dictionary<UserRole, IReadOnlyCollection<UserPermission>>
        {
            [UserRole.SystemAdmin] = Enum.GetValues<UserPermission>(),
            [UserRole.DepartmentManager] = new[]
            {
                UserPermission.ManageDepartments,
                UserPermission.ViewIncomingDocuments,
                UserPermission.ApproveDocuments,
                UserPermission.RejectDocuments,
                UserPermission.ExportReports,
                UserPermission.ViewReports,
                UserPermission.DesignReports,
                UserPermission.ViewDashboards,
                UserPermission.DesignDashboards,
                UserPermission.ManageWorkOrders,
                UserPermission.ReleaseWorkOrders
            },
            [UserRole.Approver] = new[]
            {
                UserPermission.ViewIncomingDocuments,
                UserPermission.ApproveDocuments,
                UserPermission.RejectDocuments,
                UserPermission.ViewReports,
                UserPermission.ViewDashboards
            },
            [UserRole.Operator] = new[]
            {
                UserPermission.ViewIncomingDocuments,
                UserPermission.ViewReports,
                UserPermission.ViewDashboards,
                UserPermission.ManageWorkOrders,
                UserPermission.ReportProduction,
                UserPermission.OperateMachine
            },
            [UserRole.Auditor] = new[]
            {
                UserPermission.ViewIncomingDocuments,
                UserPermission.ExportReports,
                UserPermission.ViewAuditLogs,
                UserPermission.ViewReports,
                UserPermission.ViewDashboards
            }
        };

    public static IReadOnlyCollection<UserRole> Roles { get; } = Enum.GetValues<UserRole>();
    public static IReadOnlyCollection<UserPermission> Permissions { get; } = Enum.GetValues<UserPermission>();

    public static IReadOnlyCollection<UserPermission> GetAllowedPermissions(UserRole role) =>
        AllowedPermissionsByRole.GetValueOrDefault(role, Array.Empty<UserPermission>());

    public static bool IsPermissionAllowed(UserRole role, UserPermission permission) =>
        GetAllowedPermissions(role).Contains(permission);

    // ClaimTypes.Role hem enum adi ("SystemAdmin") hem de Turkce label
    // ("Sistem Yoneticisi") seklinde gelebilir. Iki formati da kabul eder.
    public static bool TryParseRole(string? value, out UserRole role)
    {
        role = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (Enum.TryParse(value, ignoreCase: true, out role)) return true;

        foreach (var candidate in Roles)
        {
            if (string.Equals(GetRoleLabel(candidate), value, StringComparison.OrdinalIgnoreCase))
            {
                role = candidate;
                return true;
            }
        }
        return false;
    }

    public static string GetRoleLabel(UserRole role) =>
        role switch
        {
            UserRole.SystemAdmin => "Sistem Yoneticisi",
            UserRole.DepartmentManager => "Departman Yoneticisi",
            UserRole.Approver => "Onaylayici",
            UserRole.Operator => "Operator",
            UserRole.Auditor => "Denetci",
            _ => role.ToString()
        };

    public static string GetPermissionLabel(UserPermission permission) =>
        permission switch
        {
            UserPermission.ManageIntegratorSettings => "Entegrator Ayarlari Yonetimi",
            UserPermission.ManageDepartments => "Departman Yonetimi",
            UserPermission.ManageUsers => "Kullanici Yonetimi",
            UserPermission.ViewIncomingDocuments => "Gelen Dokumanlari Goruntuleme",
            UserPermission.ApproveDocuments => "Dokuman Onaylama",
            UserPermission.RejectDocuments => "Dokuman Reddetme",
            UserPermission.ExportReports => "Rapor Aktarimi",
            UserPermission.ViewAuditLogs => "Denetim Kayitlarini Goruntuleme",
            UserPermission.ManageCompanySettings => "Sirket Tanimlama Yonetimi",
            UserPermission.ViewReports => "Rapor Goruntuleme",
            UserPermission.DesignReports => "Rapor Tasarlama",
            UserPermission.ManageReports => "Rapor Yonetimi",
            UserPermission.ViewDashboards => "Pano Goruntuleme",
            UserPermission.DesignDashboards => "Pano Tasarlama",
            UserPermission.ManageWorkOrders => "Is Emri Yonetimi",
            UserPermission.ReleaseWorkOrders => "Is Emri Salma (Release)",
            UserPermission.ReportProduction => "Uretim Hareketi Bildirme",
            UserPermission.OperateMachine => "Makine Operatorlugu (Shop-Floor)",
            _ => permission.ToString()
        };
}
