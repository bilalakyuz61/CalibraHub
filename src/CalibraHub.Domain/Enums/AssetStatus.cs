using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>
/// Varlık yaşam döngüsü durumu. SmartBoard kartında renkli rozet olarak gösterilir
/// (Aktif=emerald, Bakımda=amber, Hurda=slate, Elden Çıkarıldı=rose).
/// </summary>
public enum AssetStatus : byte
{
    [Description("Aktif")]
    Active = 0,

    [Description("Bakımda")]
    InMaintenance = 1,

    [Description("Hurda")]
    Retired = 2,

    [Description("Elden Çıkarıldı")]
    Disposed = 3,
}
