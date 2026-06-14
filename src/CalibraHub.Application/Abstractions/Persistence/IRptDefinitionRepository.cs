using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IRptDefinitionRepository
{
    Task<IReadOnlyCollection<ReportDefinitionSummaryDto>> GetAccessibleAsync(
        int userId,
        IReadOnlyCollection<UserRole> roles,
        CancellationToken ct);

    Task<RptDefinition?> GetByIdAsync(int id, CancellationToken ct);
    Task<RptDefinition?> GetByCodeAsync(string code, CancellationToken ct);
    Task<IReadOnlyCollection<RptDefinitionRole>> GetRolesAsync(int defId, CancellationToken ct);
    Task<int> UpsertAsync(UpsertRptDefinitionRequest req, CancellationToken ct);
    Task ReplaceRolesAsync(int defId, IReadOnlyCollection<RptDefRoleDto> roles, CancellationToken ct);
    Task SoftDeleteAsync(int id, CancellationToken ct);
}
