using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>DÖF (CAPA) yaşam döngüsü durumu (tek otorite Capa.Status'te).</summary>
public enum CapaStatus : byte
{
    [Description("Açık")]
    Acik = 0,

    [Description("Kök Neden Analizi")]
    KokNedenAnaliz = 1,

    [Description("Aksiyonda")]
    Aksiyonda = 2,

    [Description("Doğrulama Bekliyor")]
    DogrulamaBekliyor = 3,

    [Description("Kapalı")]
    Kapali = 4,

    [Description("İptal")]
    Iptal = 5,
}
