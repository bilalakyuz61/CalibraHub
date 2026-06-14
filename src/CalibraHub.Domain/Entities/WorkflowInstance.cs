using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class WorkflowInstance
{
    public int    Id           { get; set; }
    public int    DefinitionId { get; init; }
    public string SourceType   { get; init; } = "Document"; // "Document" | "Form"
    public int    SourceId     { get; init; }
    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Pending;
    public DateTime  StartedAt   { get; set; }
    public string?   StartedBy   { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string?   ContextJson { get; set; }
    public int?      CreatedById { get; init; }
    public DateTime  Created     { get; init; } = DateTime.UtcNow;
    public int?      UpdatedById { get; set; }
    public DateTime? Updated     { get; set; }

    private readonly List<WorkflowInstanceNode> _nodes = [];
    public IReadOnlyList<WorkflowInstanceNode> Nodes => _nodes;

    public void AddNode(WorkflowInstanceNode node) => _nodes.Add(node);

    public void Start(string? contextJson, string? startedBy)
    {
        Status      = WorkflowInstanceStatus.Active;
        StartedAt   = DateTime.UtcNow;
        StartedBy   = startedBy;
        ContextJson = contextJson;
        Updated     = DateTime.UtcNow;
    }

    public void Complete()
    {
        Status      = WorkflowInstanceStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        Updated     = DateTime.UtcNow;
    }

    public void Cancel(string? actionBy, string? reason)
    {
        Status      = WorkflowInstanceStatus.Cancelled;
        CompletedAt = DateTime.UtcNow;
        Updated     = DateTime.UtcNow;
        foreach (var n in _nodes.Where(n => n.Status == WorkflowInstanceNodeStatus.Active ||
                                             n.Status == WorkflowInstanceNodeStatus.Pending))
        {
            n.Status      = WorkflowInstanceNodeStatus.Skipped;
            n.Action      = "Skipped";
            n.ActionBy    = actionBy;
            n.Note        = reason;
            n.CompletedAt = DateTime.UtcNow;
        }
    }
}
