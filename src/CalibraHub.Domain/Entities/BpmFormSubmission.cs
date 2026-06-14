namespace CalibraHub.Domain.Entities;

public sealed class BpmFormSubmission
{
    public int      Id                 { get; set; }
    public int      FormDefinitionId   { get; init; }
    public string?  SubmittedBy        { get; set; }
    public DateTime SubmittedAt        { get; set; } = DateTime.UtcNow;
    public string   Status             { get; set; } = "Draft"; // Draft|Submitted|InProgress|Approved|Rejected
    public int?     WorkflowInstanceId { get; set; }
    public int?     CreatedById        { get; set; }
    public DateTime Created            { get; set; } = DateTime.UtcNow;
    public int?     UpdatedById        { get; set; }
    public DateTime? Updated           { get; set; }

    private readonly List<BpmFormSubmissionValue> _values = [];
    public IReadOnlyList<BpmFormSubmissionValue> Values => _values;

    public void AddValue(BpmFormSubmissionValue value) => _values.Add(value);

    public void Submit(string? submittedBy)
    {
        Status      = "Submitted";
        SubmittedBy = submittedBy;
        SubmittedAt = DateTime.UtcNow;
        Updated     = DateTime.UtcNow;
    }

    public void LinkWorkflow(int instanceId)
    {
        WorkflowInstanceId = instanceId;
        Status             = "InProgress";
        Updated            = DateTime.UtcNow;
    }

    public void Complete(bool approved)
    {
        Status  = approved ? "Approved" : "Rejected";
        Updated = DateTime.UtcNow;
    }
}
