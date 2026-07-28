using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>Muayene tipi — planın ve kaydın hangi aşamada uygulandığı.</summary>
public enum InspectionType : byte
{
    [Description("Gelen Kalite (Mal Kabul)")]
    Incoming = 1,

    [Description("Proses / Ara Kontrol")]
    InProcess = 2,

    [Description("Final Kalite")]
    Final = 3,

    [Description("Laboratuvar")]
    Lab = 4,
}
