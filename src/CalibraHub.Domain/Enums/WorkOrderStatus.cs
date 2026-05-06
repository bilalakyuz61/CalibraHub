using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>
/// Uretim is emri durum akisi: Planned -> Released -> InProgress -> Completed -> Closed.
/// Cancelled her durumda alinabilir (Closed haric — kapali emir iptal edilemez).
/// </summary>
public enum WorkOrderStatus : byte
{
    [Description("Taslak — duzenleme serbest")]
    Planned = 0,

    [Description("Salindi — uretime hazir")]
    Released = 1,

    [Description("Devam ediyor — ilk hareket islendi")]
    InProgress = 2,

    [Description("Tamamlandi — planlanan miktar uretildi")]
    Completed = 3,

    [Description("Kapatildi — kalici, hareket alinamaz")]
    Closed = 4,

    [Description("Iptal edildi")]
    Cancelled = 5,
}
