using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>
/// Proje türü — AR-GE (Araştırma-Geliştirme) ve ÜR-GE (Ürün Geliştirme) aynı modülde
/// tek board'dan takip edilir; bu ayraç ile birbirinden ayrilir. Yasam dongusu (ArgeProjectStatus)
/// ve tum altyapi her iki tur icin ortaktir.
/// </summary>
public enum ArgeProjectType : byte
{
    [Description("AR-GE — Araştırma-Geliştirme")]
    ArGe = 0,

    [Description("ÜR-GE — Ürün Geliştirme")]
    UrGe = 1,
}
