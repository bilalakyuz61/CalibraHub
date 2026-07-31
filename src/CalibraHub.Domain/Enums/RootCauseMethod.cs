using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>DÖF kök neden analiz yöntemi.</summary>
public enum RootCauseMethod : byte
{
    [Description("5 Neden")]
    FiveWhy = 1,

    [Description("Balık Kılçığı (Ishikawa)")]
    Ishikawa = 2,

    [Description("8D")]
    SekizD = 3,
}
