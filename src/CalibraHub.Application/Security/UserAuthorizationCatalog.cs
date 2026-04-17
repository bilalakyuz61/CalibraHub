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
                UserPermission.DesignReports
            },
            [UserRole.Approver] = new[]
            {
                UserPermission.ViewIncomingDocuments,
                UserPermission.ApproveDocuments,
                UserPermission.RejectDocuments,
                UserPermission.ViewReports
            },
            [UserRole.Operator] = new[]
            {
                UserPermission.ViewIncomingDocuments,
                UserPermission.ViewReports
            },
            [UserRole.Auditor] = new[]
            {
                UserPermission.ViewIncomingDocuments,
                UserPermission.ExportReports,
                UserPermission.ViewAuditLogs,
                UserPermission.ViewReports
            }
        };

    public static IReadOnlyCollection<UserRole> Roles { get; } = Enum.GetValues<UserRole>();
    public static IReadOnlyCollection<UserPermission> Permissions { get; } = Enum.GetValues<UserPermission>();

    public static IReadOnlyCollection<UserPermission> GetAllowedPermissions(UserRole role) =>
        AllowedPermissionsByRole.GetValueOrDefault(role, Array.Empty<UserPermission>());

    public static bool IsPermissionAllowed(UserRole role, UserPermission permission) =>
        GetAllowedPermissions(role).Contains(permission);

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
            _ => permission.ToString()
        };
}
