using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IApprovalFlowRepository
{
    Task<IReadOnlyList<ApprovalFlowSummaryDto>> GetAllSummariesAsync(CancellationToken ct);
    Task<ApprovalFlowDto?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<ApprovalFlowDto>> GetByDocumentKindAsync(string documentKind, CancellationToken ct);
    Task<int> SaveAsync(SaveApprovalFlowRequest request, int? byUserId, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<ApprovalFlowEdgeDto>> GetEdgesByFlowIdAsync(int flowId, CancellationToken ct);
}
