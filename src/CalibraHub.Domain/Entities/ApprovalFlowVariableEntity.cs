using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Onay akışı değişken şablonu — Designer'daki SetVariable ve Decision node'larında kullanılan akış-scoped değişken tanımı. Çalışma zamanı değerleri ApprovalInstanceVariable tablosunda tutulur.")]
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
