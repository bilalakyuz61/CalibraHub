using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Çalışan onay süreci değişkeni — ApprovalFlowVariable şablonundan türetilen, her instance'a özel güncel değer. Executor tarafından okunur/yazılır.")]
public sealed class ApprovalInstanceVariable
{
    public int Id { get; init; }
    public int InstanceId { get; init; }
    public required string Name { get; init; }
    public string? Value { get; init; }
    /// <summary>int | bool | string | decimal | date</summary>
    public string TypeCode { get; init; } = "int";
    public DateTime Updated { get; init; }
}
