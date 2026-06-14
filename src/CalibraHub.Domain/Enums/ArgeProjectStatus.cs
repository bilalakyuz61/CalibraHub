using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>
/// AR-GE proje yasam dongusu:
/// Planning -> Development -> Prototyping -> Testing -> DesignReview -> Approved -> TransferredToProduction.
/// Rejected, Geliştirme'ye geri donebilir (revizyon). Cancelled, terminal durumlar
/// (Approved / TransferredToProduction / Rejected) disinda her durumdan alinabilir.
/// Bu statu AR-GE icin TEK otoritedir; Document.Status AR-GE akisinda kullanilmaz.
/// </summary>
public enum ArgeProjectStatus : byte
{
    [Description("Planlama — fikir/fizibilite, duzenleme serbest")]
    Planning = 0,

    [Description("Gelistirme — tasarim calismasi suruyor")]
    Development = 1,

    [Description("Prototip — numune uretiliyor")]
    Prototyping = 2,

    [Description("Test — prototip deneme/olcum asamasinda")]
    Testing = 3,

    [Description("Tasarim Onayinda — onay bekliyor")]
    DesignReview = 4,

    [Description("Onaylandi — tasarim kilitli, uretime hazir")]
    Approved = 5,

    [Description("Uretime Aktarildi — seri uretime gecirildi (terminal)")]
    TransferredToProduction = 6,

    [Description("Reddedildi — revizyon icin gelistirmeye doner")]
    Rejected = 7,

    [Description("Iptal edildi (terminal)")]
    Cancelled = 8,
}
