using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>
/// İş emri içindeki operasyon adımının durumu (shop-floor + auto-explosion akışı).
/// Pending → InProgress → Completed; ya da başka bir adıma yer açmak için Skipped.
/// </summary>
public enum WorkOrderOperationStatus : byte
{
    [Description("Bekliyor")]
    Pending = 0,

    [Description("Devam ediyor")]
    InProgress = 1,

    [Description("Tamamlandı")]
    Completed = 2,

    [Description("Atlandı")]
    Skipped = 3,
}
