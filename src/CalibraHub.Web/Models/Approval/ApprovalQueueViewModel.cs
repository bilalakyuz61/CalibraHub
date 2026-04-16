using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Shared;

namespace CalibraHub.Web.Models.Approval;

public sealed class ApprovalQueueViewModel
{
    public required IReadOnlyCollection<PendingApprovalDocumentDto> Documents { get; init; }
    public required GridListStateViewModel ListState { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string KindTitle { get; init; } = "Tum Belgeler";
    public string PageTitle { get; init; } = "Elektronik Belgeler";
    public DateOnly DateFrom { get; init; } = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
    public DateOnly DateTo { get; init; } = DateOnly.FromDateTime(DateTime.Today);
    public object? BoardConfig { get; init; }
}
