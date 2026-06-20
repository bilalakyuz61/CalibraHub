namespace CalibraHub.Domain.Entities;

/// <summary>
/// Surec-scoped degisken tanimi (per-flow). Designer'da SetVariable node + Decision
/// kosul satirlarinda referans edilir. Per-instance state ApprovalInstanceVariable'da
/// tutulur — runtime executor okur/yazar.
/// </summary>
public sealed class ApprovalFlowVariableEntity
{
    public int Id { get; init; }
    public int FlowId { get; init; }
    public required string Name { get; init; }
    /// <summary>int | bool | string | decimal | date</summary>
    public string TypeCode { get; init; } = "int";
    public string? DefaultValue { get; init; }
    public string? Description { get; init; }
    /// <summary>manual | sql</summary>
    public string ValueSource { get; init; } = "manual";
    public string? SqlQuery { get; init; }
    public int SortOrder { get; init; }
    public DateTime Created { get; init; }
}
