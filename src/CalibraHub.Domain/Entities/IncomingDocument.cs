using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class IncomingDocument : Entity
{
    public int IntegratorSettingsId { get; init; }
    public required string EnvelopeId { get; init; }
    public required string DocumentNumber { get; init; }
    public DocumentKind Kind { get; init; }
    public DateOnly IssueDate { get; init; }
    public required string SenderTaxNumber { get; init; }
    public string? SenderName { get; init; }
    public required string RecipientTaxNumber { get; init; }
    public required string PayloadRaw { get; init; }
    public ApprovalStatus ApprovalStatus { get; private set; } = ApprovalStatus.Pending;
    public DateTime ImportedAt { get; init; } = DateTime.Now;

    public bool IsProcessed { get; set; } = false;

    public void MarkApproved() => ApprovalStatus = ApprovalStatus.Approved;
    public void MarkRejected() => ApprovalStatus = ApprovalStatus.Rejected;
    public void SetProcessed(bool processed) => IsProcessed = processed;
}
