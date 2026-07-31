using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>DÖF aksiyon satırının türü.</summary>
public enum CapaActionType : byte
{
    [Description("Acil Sınırlama")]
    AcilSinirlama = 1,

    [Description("Düzeltici")]
    Duzeltici = 2,

    [Description("Önleyici")]
    Onleyici = 3,
}
