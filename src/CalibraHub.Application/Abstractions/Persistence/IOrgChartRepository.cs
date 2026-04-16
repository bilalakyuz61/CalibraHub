using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IOrgChartRepository
{
    Task<IReadOnlyCollection<OrgChart>> GetChartsByCompanyAsync(int companyId, CancellationToken ct);
    Task<OrgChart?> GetChartByIdAsync(Guid id, CancellationToken ct);
    Task SaveChartAsync(OrgChart chart, CancellationToken ct);
    Task DeleteChartAsync(Guid id, CancellationToken ct);
    Task SetDefaultChartAsync(int companyId, Guid chartId, CancellationToken ct);

    Task<IReadOnlyCollection<OrgChartNode>> GetNodesByChartAsync(Guid chartId, CancellationToken ct);
    Task SaveNodeAsync(OrgChartNode node, CancellationToken ct);
    Task DeleteNodeAsync(Guid nodeId, CancellationToken ct);
    Task ReplaceNodesAsync(Guid chartId, IReadOnlyCollection<OrgChartNode> nodes, CancellationToken ct);
}
