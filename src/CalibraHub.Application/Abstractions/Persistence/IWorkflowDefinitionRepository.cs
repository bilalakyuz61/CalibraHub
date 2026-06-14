using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IWorkflowDefinitionRepository
{
    Task<IReadOnlyList<WorkflowDefinitionDto>> GetAllAsync(CancellationToken ct);
    Task<WorkflowDefinitionDetailDto?> GetDetailAsync(int id, CancellationToken ct);
    Task<int> SaveDefinitionAsync(WorkflowDefinition def, int? actor, CancellationToken ct);
    Task<int> SaveNodeAsync(WorkflowNode node, int? actor, CancellationToken ct);
    Task DeleteNodeAsync(int nodeId, CancellationToken ct);
    Task<int> SaveTransitionAsync(WorkflowTransition transition, int? actor, CancellationToken ct);
    Task DeleteTransitionAsync(int transitionId, CancellationToken ct);
    Task DeleteDefinitionAsync(int id, CancellationToken ct);
    Task<WorkflowDefinition?> GetRawAsync(int id, CancellationToken ct);
}
