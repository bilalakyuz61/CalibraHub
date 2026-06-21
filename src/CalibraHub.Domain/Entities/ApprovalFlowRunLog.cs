using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Onay akışı çalışma günlüğü — executor'ın her node geçişinde yazdığı adım bazlı log; hata ayıklama ve SLA izleme için kullanılır. Id BIGINT IDENTITY (yüksek hacim).")]
public sealed class ApprovalFlowRunLog
{
    public long Id { get; init; }
    public int InstanceId { get; init; }
    public int FlowId { get; init; }
    public int? NodeId { get; init; }
    public string? NodeType { get; init; }
    public string? NodeName { get; init; }
    public required string Event { get; init; }
    public string? Detail { get; init; }
    public int? DurationMs { get; init; }
    public DateTime Created { get; init; }
}
