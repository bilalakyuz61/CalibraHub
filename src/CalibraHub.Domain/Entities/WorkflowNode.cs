using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class WorkflowNode
{
    public int Id { get; set; }
    public int DefinitionId { get; init; }
    public WorkflowNodeType NodeType { get; init; }
    public required string Name { get; set; }
    public int PositionX { get; set; }
    public int PositionY { get; set; }

    // Task fields
    public WorkflowActorType? ActorType { get; set; }
    public string? ActorRefId { get; set; }
    public string? ActorExpression { get; set; }
    public int? TimeoutHours { get; set; }
    public WorkflowOnRejectPolicy? OnRejectPolicy { get; set; }

    // ParallelJoin field
    public int? JoinExpectedTokens { get; set; }

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }

    public void Move(int x, int y) { PositionX = x; PositionY = y; Updated = DateTime.UtcNow; }
    public void Rename(string name) { Name = name; Updated = DateTime.UtcNow; }
    public void ConfigureActor(WorkflowActorType type, string? refId, string? expression)
    {
        ActorType = type;
        ActorRefId = refId;
        ActorExpression = expression;
        Updated = DateTime.UtcNow;
    }
}
