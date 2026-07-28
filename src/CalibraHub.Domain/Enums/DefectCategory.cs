using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>Hata kodu kataloğu kategorisi (hata kodlarını gruplar).</summary>
public enum DefectCategory : byte
{
    [Description("Boyutsal")]
    Dimensional = 1,

    [Description("Görsel")]
    Visual = 2,

    [Description("Fonksiyonel")]
    Functional = 3,

    [Description("Malzeme")]
    Material = 4,

    [Description("Diğer")]
    Other = 5,
}
