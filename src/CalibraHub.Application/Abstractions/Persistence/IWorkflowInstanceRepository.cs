using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IWorkflowInstanceRepository
{
    Task<WorkflowInstance?> GetByIdAsync(int instanceId, CancellationToken ct = default);
    Task<WorkflowInstance?> GetActiveBySourceAsync(string sourceType, int sourceId, CancellationToken ct = default);
    Task<int> CreateAsync(WorkflowInstance instance, CancellationToken ct = default);
    Task UpdateStatusAsync(WorkflowInstance instance, CancellationToken ct = default);

    Task<WorkflowInstanceNode?> GetNodeByIdAsync(int instanceNodeId, CancellationToken ct = default);
    Task<int> CreateInstanceNodeAsync(WorkflowInstanceNode node, CancellationToken ct = default);
    Task UpdateInstanceNodeAsync(WorkflowInstanceNode node, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowInstanceNode>> GetActiveNodesByInstanceAsync(int instanceId, CancellationToken ct = default);
    Task<int> CountCompletedTokensAtNodeAsync(int instanceId, int nodeId, CancellationToken ct = default);

    Task<IReadOnlyList<(WorkflowInstanceNode Node, WorkflowInstance Instance)>> GetPendingForUserAsync(
        string userId, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowInstanceNode>> GetTimedOutActiveNodesAsync(CancellationToken ct = default);
}
