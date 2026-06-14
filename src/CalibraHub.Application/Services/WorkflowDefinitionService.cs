using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

public sealed class WorkflowDefinitionService
{
    private readonly IWorkflowDefinitionRepository _repo;

    public WorkflowDefinitionService(IWorkflowDefinitionRepository repo) => _repo = repo;

    public Task<IReadOnlyList<WorkflowDefinitionDto>> GetAllAsync(CancellationToken ct)
        => _repo.GetAllAsync(ct);

    public Task<WorkflowDefinitionDetailDto?> GetDetailAsync(int id, CancellationToken ct)
        => _repo.GetDetailAsync(id, ct);

    public async Task<int> SaveDefinitionAsync(SaveWorkflowDefinitionRequest req, int? actor, CancellationToken ct)
    {
        WorkflowDefinition def;
        if (req.Id.HasValue)
        {
            def = await _repo.GetRawAsync(req.Id.Value, ct)
                  ?? throw new InvalidOperationException($"Workflow tanımı bulunamadı: {req.Id}");
            def.Name = req.Name;
            def.Description = req.Description;
            def.DocumentTypeId = req.DocumentTypeId;
            def.IsActive = req.IsActive;
            def.UpdatedById = actor;
            def.Updated = DateTime.UtcNow;
        }
        else
        {
            def = new WorkflowDefinition
            {
                Name = req.Name,
                Description = req.Description,
                DocumentTypeId = req.DocumentTypeId,
                IsActive = req.IsActive,
                CreatedById = actor,
            };
        }
        return await _repo.SaveDefinitionAsync(def, actor, ct);
    }

    public async Task<int> SaveNodeAsync(SaveWorkflowNodeRequest req, int? actor, CancellationToken ct)
    {
        var nodeType = Enum.TryParse<WorkflowNodeType>(req.NodeType, out var nt) ? nt : WorkflowNodeType.Task;
        var actorType = req.ActorType != null && Enum.TryParse<WorkflowActorType>(req.ActorType, out var at) ? (WorkflowActorType?)at : null;
        var onRejectPolicy = req.OnRejectPolicy != null && Enum.TryParse<WorkflowOnRejectPolicy>(req.OnRejectPolicy, out var rp) ? (WorkflowOnRejectPolicy?)rp : null;

        var node = new WorkflowNode
        {
            Id = req.Id ?? 0,
            DefinitionId = req.DefinitionId,
            NodeType = nodeType,
            Name = req.Name,
            PositionX = req.PositionX,
            PositionY = req.PositionY,
            ActorType = actorType,
            ActorRefId = req.ActorRefId,
            ActorExpression = req.ActorExpression,
            TimeoutHours = req.TimeoutHours,
            OnRejectPolicy = onRejectPolicy,
            JoinExpectedTokens = req.JoinExpectedTokens,
            CreatedById = actor,
        };
        return await _repo.SaveNodeAsync(node, actor, ct);
    }

    public Task DeleteNodeAsync(int nodeId, CancellationToken ct) => _repo.DeleteNodeAsync(nodeId, ct);

    public async Task<int> SaveTransitionAsync(SaveWorkflowTransitionRequest req, int? actor, CancellationToken ct)
    {
        var transition = new WorkflowTransition
        {
            Id = req.Id ?? 0,
            DefinitionId = req.DefinitionId,
            FromNodeId = req.FromNodeId,
            ToNodeId = req.ToNodeId,
            Label = req.Label,
            Condition = req.Condition,
            Priority = req.Priority,
            IsDefault = req.IsDefault,
            CreatedById = actor,
        };
        return await _repo.SaveTransitionAsync(transition, actor, ct);
    }

    public Task DeleteTransitionAsync(int transitionId, CancellationToken ct) => _repo.DeleteTransitionAsync(transitionId, ct);

    public Task DeleteDefinitionAsync(int id, CancellationToken ct) => _repo.DeleteDefinitionAsync(id, ct);

    public async Task<WorkflowValidationResultDto> ValidateAsync(int id, CancellationToken ct)
    {
        var detail = await _repo.GetDetailAsync(id, ct);
        if (detail is null) return new WorkflowValidationResultDto(false, ["Workflow tanımı bulunamadı."]);

        var def = await _repo.GetRawAsync(id, ct);
        if (def is null) return new WorkflowValidationResultDto(false, ["Workflow tanımı bulunamadı."]);

        var errors = def.Validate();
        return new WorkflowValidationResultDto(errors.Count == 0, errors);
    }

    public async Task<int> PublishAsync(int id, int? actor, CancellationToken ct)
    {
        var def = await _repo.GetRawAsync(id, ct)
                  ?? throw new InvalidOperationException($"Workflow tanımı bulunamadı: {id}");
        def.Publish();
        def.UpdatedById = actor;
        return await _repo.SaveDefinitionAsync(def, actor, ct);
    }

    public async Task<int> CloneAsync(int id, int? actor, CancellationToken ct)
    {
        var def = await _repo.GetRawAsync(id, ct)
                  ?? throw new InvalidOperationException($"Workflow tanımı bulunamadı: {id}");
        var clone = def.CreateNewVersion(actor);
        return await _repo.SaveDefinitionAsync(clone, actor, ct);
    }
}
