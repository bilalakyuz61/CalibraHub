using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>Operation.StandardDuration ve WorkOrderOperation.PlannedDuration için süre birimi.</summary>
public enum DurationUnit : byte
{
    [Description("Dakika")]
    Minute = 1,

    [Description("Saat")]
    Hour = 2,
}
