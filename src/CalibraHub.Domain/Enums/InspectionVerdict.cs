using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>Muayenenin genel kararı — ölçüm satırlarından otomatik türetilir.</summary>
public enum InspectionVerdict : byte
{
    [Description("Uygun")]
    Conforming = 1,

    [Description("Uygunsuz")]
    NonConforming = 2,

    [Description("Şartlı Kabul")]
    Conditional = 3,
}
