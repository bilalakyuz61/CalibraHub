using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>DÖF (CAPA) kaydının önem derecesi.</summary>
public enum CapaSeverity : byte
{
    [Description("Düşük")]
    Dusuk = 1,

    [Description("Orta")]
    Orta = 2,

    [Description("Yüksek")]
    Yuksek = 3,

    [Description("Kritik")]
    Kritik = 4,
}
