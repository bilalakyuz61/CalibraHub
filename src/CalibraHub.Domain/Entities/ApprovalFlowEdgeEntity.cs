namespace CalibraHub.Domain.Entities;

public sealed class ApprovalFlowEdgeEntity
{
    public int Id { get; init; }
    public int FlowId { get; init; }
    public int SourceStepId { get; init; }
    public int TargetStepId { get; init; }
    public string? Label { get; init; }
    public string EdgeKind { get; init; } = "default"; // 'default'|'true'|'false'|'custom'
    public string? Condition { get; init; }
    public int SortOrder { get; init; }
    public DateTime Created { get; init; }
}
