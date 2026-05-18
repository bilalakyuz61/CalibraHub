namespace CalibraHub.Domain.Enums;

/// <summary>
/// Bir entegrasyon X kayit Y eslemesi icin "Aktarim Kuyrugu" durum etiketi.
/// </summary>
public enum IntegrationRecordStatusType
{
    /// <summary>Hic islenmemis — kuyrukta gorunur, aktarilabilir.</summary>
    Pending = 0,

    /// <summary>Basariyla gonderildi — kuyruktan kaybolur.</summary>
    Sent = 1,

    /// <summary>Denendi, hata aldi — kuyrukta gorunur, yeniden denenebilir.</summary>
    Failed = 2,

    /// <summary>
    /// Kullanici manuel "haric tut" dedi (ornegin: bu kayit zaten ERP'de var,
    /// CalibraHub'a sonradan eklendi). Kuyrukta gorunmez. "Haric Tutulanlar"
    /// filtresinden geri alinabilir.
    /// </summary>
    Skipped = 3,
}
