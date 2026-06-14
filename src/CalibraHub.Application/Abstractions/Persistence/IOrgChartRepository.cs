using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IOrgChartRepository
{
    Task<IReadOnlyCollection<OrgChart>> GetChartsByCompanyAsync(int companyId, CancellationToken ct);
    Task<OrgChart?> GetChartByIdAsync(int id, CancellationToken ct);
    Task SaveChartAsync(OrgChart chart, CancellationToken ct);
    Task DeleteChartAsync(int id, CancellationToken ct);
    Task SetDefaultChartAsync(int companyId, int chartId, CancellationToken ct);

    Task<IReadOnlyCollection<OrgChartNode>> GetNodesByChartAsync(int chartId, CancellationToken ct);
    Task SaveNodeAsync(OrgChartNode node, CancellationToken ct);
    Task DeleteNodeAsync(int nodeId, CancellationToken ct);
    Task ReplaceNodesAsync(int chartId, IReadOnlyCollection<OrgChartNode> nodes, CancellationToken ct);

    /// <summary>
    /// Tek node'u yeni parent altına taşır + sortOrder günceller.
    /// Cycle kontrolü çağıran tarafın (OrgChartDomainService) sorumluluğundadır.
    /// </summary>
    Task MoveNodeAsync(int nodeId, int? newParentNodeId, int newSortOrder, CancellationToken ct);
}
