using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Services;

public interface IReportDashboardService
{
    /// <summary>
    /// Grafana listesini DB'ye senkronize eder, ardından mevcut kullanıcının görebileceği
    /// panoları döner. DesignDashboards yetkisi varsa kısıtlamalar atlanır.
    /// </summary>
    Task<IReadOnlyList<GrafanaDashboardSummary>> GetAccessibleAsync(
        IReadOnlyList<GrafanaDashboardSummary> grafanaDashboards,
        int userId, int? departmentId, UserRole role,
        string? actor, CancellationToken ct);

    /// <summary>Admin: erişim ayarlarıyla birlikte tüm panoları döner.</summary>
    Task<IReadOnlyList<DashboardAccessDto>> GetAllWithAccessAsync(
        IReadOnlyList<GrafanaDashboardSummary> grafanaDashboards,
        string? actor, CancellationToken ct);

    Task SetAccessAsync(string grafanaUid, IReadOnlyList<int> userIds, IReadOnlyList<int> deptIds, string? actor, CancellationToken ct);
}

public sealed record DashboardAccessDto(
    int                  Id,
    string               GrafanaUid,
    string               Title,
    string?              FolderTitle,
    bool                 IsRestricted,
    IReadOnlyList<int>   AllowedUserIds,
    IReadOnlyList<int>   AllowedDepartmentIds);
