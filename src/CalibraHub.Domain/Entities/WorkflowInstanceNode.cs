using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class WorkflowInstanceNode
{
    public int    Id             { get; set; }
    public int    InstanceId     { get; init; }
    public int    NodeId         { get; init; }
    public WorkflowInstanceNodeStatus Status { get; set; } = WorkflowInstanceNodeStatus.Pending;
    public string? AssignedUserId { get; set; }
    public DateTime? EnteredAt   { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Action        { get; set; }  // Approve|Reject|ReturnToSender|Skipped
    public string? ActionBy      { get; set; }
    public string? Note          { get; set; }
    public int? CreatedById      { get; init; }
    public DateTime Created      { get; init; } = DateTime.UtcNow;
    public int? UpdatedById      { get; set; }
    public DateTime? Updated     { get; set; }

    // Runtime helpers — not persisted
    public string? NodeName      { get; set; }
    public WorkflowNodeType NodeType { get; set; }
}
