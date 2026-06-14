using CalibraHub.Application.Contracts;

namespace CalibraHub.Web.Models.Approval;

public sealed class ApprovalPanelViewModel
{
    public Guid DocumentId { get; init; }
    public required string DocumentNumber { get; init; }
    public required string DocumentKind { get; init; }
    public ApprovalInstanceDto? Instance { get; init; }
    public IReadOnlyList<ApprovalFlowSummaryDto> AvailableFlows { get; init; } = Array.Empty<ApprovalFlowSummaryDto>();
    public required string CurrentUserId { get; init; }
    public required string CurrentUserName { get; init; }
}
