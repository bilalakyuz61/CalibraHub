namespace CalibraHub.Domain.Entities;

public sealed class WorkflowTransition
{
    public int Id { get; set; }
    public int DefinitionId { get; init; }
    public int FromNodeId { get; init; }
    public int ToNodeId { get; init; }
    public string? Label { get; set; }
    public string? Condition { get; set; }
    public int Priority { get; set; }
    public bool IsDefault { get; set; }

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }

    public void UpdateCondition(string? condition) { Condition = condition; Updated = DateTime.UtcNow; }
    public void SetPriority(int priority) { Priority = priority; Updated = DateTime.UtcNow; }
    public void MarkAsDefault() { IsDefault = true; Updated = DateTime.UtcNow; }
}
