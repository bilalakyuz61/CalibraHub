using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>Document (teklif/siparis/fatura) yasam dongusu durumlari.</summary>
public enum DocumentStatus
{
    [Description("Taslak — henuz gonderilmemis")]
    Draft = 0,

    [Description("Gonderildi — carinin cevabi bekleniyor")]
    Sent = 1,

    [Description("Onaylandi — siparise/faturaya donusturulebilir")]
    Approved = 2,

    [Description("Reddedildi — cari kabul etmedi")]
    Rejected = 3,

    [Description("Revize edildi — yeni versiyon olusturuldu")]
    Revised = 4,

    [Description("Iptal edildi")]
    Cancelled = 5,
}
