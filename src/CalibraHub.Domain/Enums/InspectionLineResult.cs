using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>Tek ölçüm satırının sonucu (sayısal satırda tolerans karşı otomatik hesaplanır).</summary>
public enum InspectionLineResult : byte
{
    [Description("Değerlendirilmedi")]
    NotEvaluated = 0,

    [Description("Uygun")]
    Conforming = 1,

    [Description("Uygunsuz")]
    NonConforming = 2,
}
