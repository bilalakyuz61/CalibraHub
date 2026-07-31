using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>DÖF (CAPA) kaydının türü.</summary>
public enum CapaType : byte
{
    [Description("Düzeltici")]
    Duzeltici = 1,

    [Description("Önleyici")]
    Onleyici = 2,

    [Description("8D")]
    SekizD = 3,
}
