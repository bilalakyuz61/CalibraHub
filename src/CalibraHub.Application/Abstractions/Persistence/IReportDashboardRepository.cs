using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IReportDashboardRepository
{
    /// <summary>Grafana'dan gelen pano listesini DB'ye upsert eder; artık mevcut olmayanları pasif yapar.</summary>
    Task SyncAsync(IReadOnlyList<GrafanaDashboardSummary> dashboards, string? actor, CancellationToken ct);

    Task<IReadOnlyList<ReportDashboard>> GetAllActiveAsync(CancellationToken ct);

    /// <summary>Kısıtlı panolar için dashboardId → (userIds, deptIds) haritası döner. Girişi olmayan pano herkese açık.</summary>
    Task<IReadOnlyDictionary<int, (IReadOnlyList<int> UserIds, IReadOnlyList<int> DeptIds)>> GetAllRestrictionsAsync(CancellationToken ct);

    Task ReplaceAccessAsync(int dashboardId, IReadOnlyList<int> userIds, IReadOnlyList<int> deptIds, string? actor, CancellationToken ct);
}
