using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("İş emri içindeki operasyon adımı (shop-floor kuyruğu birimi). Release'te Routing'in RoutingOperation listesinden auto-explosion ile kopyalanır. Operatör tablette Başlat / Kısmi Bitir / Bitir aksiyonları çalıştırır.")]
public sealed class WorkOrderOperation
{
    public int Id { get; init; }
    public int WorkOrderId { get; init; }
    public int Sequence { get; init; }
    public int OperationId { get; init; }
    public int? MachineId { get; init; }
    public decimal? PlannedDuration { get; init; }
    public DurationUnit DurationUnit { get; init; } = DurationUnit.Minute;
    public decimal? ActualDuration { get; init; }
    public decimal ProducedQuantity { get; init; }
    public decimal ScrapQuantity { get; init; }
    public WorkOrderOperationStatus Status { get; init; }

    /// <summary>İşi başlatan personnel (Personnel.Id). Sistem User'i değil — fabrika kartı.</summary>
    public int? StartedByPersonnelId { get; init; }
    public DateTime? StartedAt { get; init; }
    public int? CompletedByPersonnelId { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Notes { get; init; }
}
