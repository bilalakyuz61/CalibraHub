using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>
/// Varlık geçmiş kaydı (AssetEvent) türü — bakım, kalibrasyon, onarım, muayene,
/// lokasyon/zimmet hareketi ve durum değişikliği.
/// </summary>
public enum AssetEventType : byte
{
    [Description("Bakım")]
    Maintenance = 0,

    [Description("Kalibrasyon")]
    Calibration = 1,

    [Description("Onarım")]
    Repair = 2,

    [Description("Muayene / Kontrol")]
    Inspection = 3,

    [Description("Hareket / Zimmet")]
    Transfer = 4,

    [Description("Durum Değişikliği")]
    StatusChange = 5,

    [Description("Diğer")]
    Other = 9,
}
