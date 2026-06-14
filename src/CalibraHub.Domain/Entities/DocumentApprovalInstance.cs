using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class DocumentApprovalInstanceEntity
{
    public int Id { get; init; }
    public Guid DocumentId { get; init; }
    public int FlowId { get; init; }
    public ApprovalInstanceStatus Status { get; init; } = ApprovalInstanceStatus.Pending;
    public int CurrentStep { get; init; } = 1;
    public string? StartedBy { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? RejectNote { get; init; }
    public bool IsActive { get; init; } = true;
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; }
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }
}
