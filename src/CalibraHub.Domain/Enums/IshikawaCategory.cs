using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>Ishikawa (Balık Kılçığı) kök neden analizinde kullanılan 6M kategorisi.
/// Yalnız CapaRootCauseItem.Method = Ishikawa olduğunda dolu; 5 Neden'de kullanılmaz (NULL).</summary>
public enum IshikawaCategory : byte
{
    [Description("İnsan")]
    Insan = 1,

    [Description("Makine")]
    Makine = 2,

    [Description("Metot")]
    Metot = 3,

    [Description("Malzeme")]
    Malzeme = 4,

    [Description("Ölçüm")]
    Olcum = 5,

    [Description("Çevre")]
    Cevre = 6,
}
