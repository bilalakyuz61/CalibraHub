using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>
/// Uygunsuz muayene için verilen karar (NCR sonucu). YALNIZCA bir karar kaydıdır —
/// hiçbir stok hareketi/blok tetiklemez (blokaj stok katmanının işidir, çıkışta enforce edilir).
/// </summary>
public enum InspectionDisposition : byte
{
    [Description("Kabul")]
    Accept = 1,

    [Description("Ret")]
    Reject = 2,

    [Description("Yeniden İşlem")]
    Rework = 3,

    [Description("Sapma İzni")]
    Concession = 4,
}
