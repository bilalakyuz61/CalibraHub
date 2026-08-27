using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class DocumentApproval : Entity
{
    // INT: IncomingDocument PK'si INT IDENTITY (bkz. IncomingDocument ozeti).
    public int IncomingDocumentId { get; init; }
    public int ApproverUserId { get; init; }
    public int StepOrder { get; init; }
    public ApprovalStatus Status { get; private set; } = ApprovalStatus.Pending;
    public DateTime? ActionDate { get; private set; }
    public string? Note { get; private set; }

    public void Approve(string? note = null)
    {
        Status = ApprovalStatus.Approved;
        Note = note;
        ActionDate = DateTime.Now;
    }

    public void Reject(string? note = null)
    {
        Status = ApprovalStatus.Rejected;
        Note = note;
        ActionDate = DateTime.Now;
    }
}
