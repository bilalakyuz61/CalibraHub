using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("Routing içindeki tek operasyon adımı. Sequence ile sıralanır. OverrideDuration boş ise Operation.StandardDuration kullanılır; dolu ise rota seviyesinde override eder. MachineId opsiyonel default makine.")]
public sealed class RoutingOperation
{
    public int Id { get; init; }
    public int RoutingId { get; init; }
    public int Sequence { get; init; }
    public int OperationId { get; init; }
    public int? MachineId { get; init; }
    public decimal? OverrideDuration { get; init; }
    public DurationUnit DurationUnit { get; init; } = DurationUnit.Minute;
    public string? Notes { get; init; }
}
