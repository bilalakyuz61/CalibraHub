using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>Document (teklif/siparis/fatura) yasam dongusu durumlari.</summary>
public enum DocumentStatus
{
    [Description("Taslak — hazirlaniyor, henuz gonderilmemis")]
    Draft = 0,

    [Description("Onay Bekliyor — onay akisina dusuruldu")]
    Sent = 1,

    [Description("Onaylandi — islem tamamlandi veya onay akisi onayladi")]
    Approved = 2,

    [Description("Reddedildi — onay akisi veya kullanici reddetti")]
    Rejected = 3,

    [Description("Revize edildi — yeni versiyon olusturuldu")]
    Revised = 4,

    [Description("Kapatildi / Iptal edildi")]
    Cancelled = 5,

    [Description("Baska belgeye donusturuldu — kaynak belge kilitlendi (sistem dahili)")]
    Converted = 6,
}
