using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

public sealed class ReportDashboardService : IReportDashboardService
{
    private readonly IReportDashboardRepository _repo;

    public ReportDashboardService(IReportDashboardRepository repo)
    {
        _repo = repo;
    }

    public async Task<IReadOnlyList<GrafanaDashboardSummary>> GetAccessibleAsync(
        IReadOnlyList<GrafanaDashboardSummary> grafanaDashboards,
        int userId, int? departmentId, UserRole role,
        string? actor, CancellationToken ct)
    {
        if (grafanaDashboards.Count == 0) return grafanaDashboards;

        await _repo.SyncAsync(grafanaDashboards, actor, ct);

        // DesignDashboards rolleri (SystemAdmin, DepartmentManager) kısıtlamayı atlar
        if (role is UserRole.SystemAdmin or UserRole.DepartmentManager)
            return grafanaDashboards;

        var allInDb      = await _repo.GetAllActiveAsync(ct);
        var restrictions = await _repo.GetAllRestrictionsAsync(ct);
        var uidToId      = allInDb.ToDictionary(d => d.GrafanaUid, d => d.Id);

        return grafanaDashboards
            .Where(g =>
            {
                if (!uidToId.TryGetValue(g.Uid, out var dbId)) return true;          // DB'de yok → public
                if (!restrictions.TryGetValue(dbId, out var access))  return true;   // kısıtlama yok → public
                return access.UserIds.Contains(userId) ||
                       (departmentId.HasValue && access.DeptIds.Contains(departmentId.Value));
            })
            .ToList();
    }

    public async Task<IReadOnlyList<DashboardAccessDto>> GetAllWithAccessAsync(
        IReadOnlyList<GrafanaDashboardSummary> grafanaDashboards,
        string? actor, CancellationToken ct)
    {
        if (grafanaDashboards.Count > 0)
            await _repo.SyncAsync(grafanaDashboards, actor, ct);

        var all          = await _repo.GetAllActiveAsync(ct);
        var restrictions = await _repo.GetAllRestrictionsAsync(ct);
        var grafanaUids  = grafanaDashboards.Select(g => g.Uid).ToHashSet();

        // Grafana'da artık olmayan ama DB'de kalan kayıtları da göster (pasif değil)
        return all
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Title)
            .Select(d =>
            {
                var isRestricted = restrictions.TryGetValue(d.Id, out var access);
                return new DashboardAccessDto(
                    d.Id,
                    d.GrafanaUid,
                    d.Title,
                    d.FolderTitle,
                    IsRestricted:        isRestricted,
                    AllowedUserIds:      isRestricted ? access.UserIds : (IReadOnlyList<int>)[],
                    AllowedDepartmentIds: isRestricted ? access.DeptIds : (IReadOnlyList<int>)[]);
            })
            .ToList();
    }

    public async Task SetAccessAsync(string grafanaUid, IReadOnlyList<int> userIds, IReadOnlyList<int> deptIds, string? actor, CancellationToken ct)
    {
        var all = await _repo.GetAllActiveAsync(ct);
        var dashboard = all.FirstOrDefault(d => d.GrafanaUid == grafanaUid)
            ?? throw new InvalidOperationException($"Pano kayıtlı değil: '{grafanaUid}'");
        await _repo.ReplaceAccessAsync(dashboard.Id, userIds, deptIds, actor, ct);
    }
}
