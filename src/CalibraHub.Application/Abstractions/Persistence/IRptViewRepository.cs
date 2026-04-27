using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IRptViewRepository
{
    Task<IReadOnlyCollection<RptView>> GetAllAsync(bool includeInactive, CancellationToken ct);
    Task<RptView?> GetByIdAsync(int id, CancellationToken ct);
    Task<RptView?> GetByCodeAsync(string code, CancellationToken ct);
    Task<IReadOnlyCollection<RptViewColumn>> GetColumnsAsync(int viewId, CancellationToken ct);
    Task<IReadOnlyCollection<RptViewRole>> GetRolesAsync(int viewId, CancellationToken ct);
    Task<int> UpsertViewAsync(UpsertRptViewRequest req, CancellationToken ct);
    Task ReplaceColumnsAsync(int viewId, IReadOnlyCollection<UpsertRptViewColumnRequest> cols, CancellationToken ct);
    Task ReplaceRolesAsync(int viewId, IReadOnlyCollection<UpsertRptViewRoleRequest> roles, CancellationToken ct);
    Task<IReadOnlyCollection<DiscoveredColumnDto>> DiscoverColumnsAsync(int viewId, CancellationToken ct);
}
