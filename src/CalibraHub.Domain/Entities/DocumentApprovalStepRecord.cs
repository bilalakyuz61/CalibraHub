using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class DocumentApprovalStepRecordEntity
{
    public int Id { get; init; }
    public int InstanceId { get; init; }
    public int StepOrder { get; init; }
    public required string StepName { get; init; }
    public ApprovalStepStatus Status { get; init; } = ApprovalStepStatus.Pending;
    public string? ApproverId { get; init; }
    public string? ApproverName { get; init; }
    public string? Note { get; init; }
    public DateTime? ActionDate { get; init; }
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; }
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }
}
