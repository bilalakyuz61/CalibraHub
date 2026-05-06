using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

public enum WorkOrderPriority : byte
{
    [Description("Dusuk")]
    Low = 0,

    [Description("Normal")]
    Medium = 1,

    [Description("Yuksek")]
    High = 2,
}
