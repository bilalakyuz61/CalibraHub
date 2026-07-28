using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>Muayene kaydı yaşam döngüsü (tek otorite companion QualityInspection.Status'te).</summary>
public enum InspectionStatus : byte
{
    [Description("Taslak")]
    Draft = 0,

    [Description("Kontrolde")]
    InProgress = 1,

    [Description("Tamamlandı")]
    Completed = 2,
}
