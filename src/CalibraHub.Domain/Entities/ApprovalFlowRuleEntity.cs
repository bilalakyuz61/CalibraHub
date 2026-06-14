using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class ApprovalFlowRuleEntity
{
    public int Id { get; init; }
    public int FlowId { get; init; }
    public ApprovalRuleType RuleType { get; init; }
    public string? RuleValue { get; init; }
    public bool IsActive { get; init; } = true;
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; }
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }
}
