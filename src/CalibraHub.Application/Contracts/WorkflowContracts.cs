namespace CalibraHub.Application.Contracts;

public sealed record WorkflowDefinitionDto(
    int Id,
    string Name,
    string? Description,
    int? DocumentTypeId,
    bool IsActive,
    int Version,
    bool IsPublished,
    DateTime Created,
    int? CreatedById);

public sealed record WorkflowNodeDto(
    int Id,
    int DefinitionId,
    string NodeType,
    string Name,
    int PositionX,
    int PositionY,
    string? ActorType,
    string? ActorRefId,
    string? ActorExpression,
    int? TimeoutHours,
    string? OnRejectPolicy,
    int? JoinExpectedTokens);

public sealed record WorkflowTransitionDto(
    int Id,
    int DefinitionId,
    int FromNodeId,
    int ToNodeId,
    string? Label,
    string? Condition,
    int Priority,
    bool IsDefault);

public sealed record WorkflowDefinitionDetailDto(
    WorkflowDefinitionDto Definition,
    IReadOnlyList<WorkflowNodeDto> Nodes,
    IReadOnlyList<WorkflowTransitionDto> Transitions);

public sealed record SaveWorkflowDefinitionRequest(
    int? Id,
    string Name,
    string? Description,
    int? DocumentTypeId,
    bool IsActive = true);

public sealed record SaveWorkflowNodeRequest(
    int? Id,
    int DefinitionId,
    string NodeType,
    string Name,
    int PositionX,
    int PositionY,
    string? ActorType,
    string? ActorRefId,
    string? ActorExpression,
    int? TimeoutHours,
    string? OnRejectPolicy,
    int? JoinExpectedTokens);

public sealed record SaveWorkflowTransitionRequest(
    int? Id,
    int DefinitionId,
    int FromNodeId,
    int ToNodeId,
    string? Label,
    string? Condition,
    int Priority,
    bool IsDefault);

public sealed record WorkflowValidationResultDto(
    bool Valid,
    IReadOnlyList<string> Errors);

// ── Runtime contracts ──────────────────────────────────────────────────────

public sealed record WorkflowInstanceDto(
    int Id,
    int DefinitionId,
    string DefinitionName,
    string SourceType,
    int SourceId,
    string Status,
    DateTime StartedAt,
    string? StartedBy,
    DateTime? CompletedAt);

public sealed record WorkflowInstanceNodeDto(
    int Id,
    int InstanceId,
    int NodeId,
    string NodeName,
    string NodeType,
    string Status,
    string? AssignedUserId,
    DateTime? EnteredAt,
    DateTime? CompletedAt,
    string? Action,
    string? ActionBy,
    string? Note);

public sealed record WorkflowInstanceDetailDto(
    WorkflowInstanceDto Instance,
    IReadOnlyList<WorkflowInstanceNodeDto> Nodes);

public sealed record StartWorkflowInstanceRequest(
    string SourceType,
    int SourceId,
    int DefinitionId,
    string? StartedBy);

public sealed record WorkflowApproveStepRequest(
    int InstanceNodeId,
    string? Note,
    string? ActionBy);

public sealed record WorkflowRejectStepRequest(
    int InstanceNodeId,
    string? Reason,
    string? ActionBy);

public sealed record ReturnStepRequest(
    int InstanceNodeId,
    int TargetNodeId,
    string? Reason,
    string? ActionBy);

public sealed record CancelInstanceRequest(
    int InstanceId,
    string? Reason,
    string? ActionBy);

public sealed record PendingTaskDto(
    int InstanceNodeId,
    int InstanceId,
    string SourceType,
    int SourceId,
    string SourceTitle,
    string WorkflowName,
    string NodeName,
    DateTime EnteredAt,
    int? TimeoutHours);
