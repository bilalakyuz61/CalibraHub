using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>DÖF aksiyon satırının yaşam döngüsü durumu.</summary>
public enum CapaActionStatus : byte
{
    [Description("Planlandı")]
    Planlandi = 0,

    [Description("Devam Ediyor")]
    DevamEdiyor = 1,

    [Description("Tamamlandı")]
    Tamamlandi = 2,

    [Description("İptal")]
    Iptal = 3,
}
