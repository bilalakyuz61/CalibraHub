namespace CalibraHub.Domain.Enums;

/// <summary>
/// Proje görevi durumu. "Sırada Bekliyor" DB'de SAKLANMAZ — proje sıralı ilerleme
/// modundayken (ArgeProject.SequentialTasks=1) önünde açık görev bulunan görev için
/// runtime'da türetilir (IsBlocked). Böylece sıra/araya ekleme değişikliklerinde
/// durum senkron tutma (drift) riski olmaz.
/// </summary>
public enum ProjectTaskStatus : byte
{
    /// <summary>Açık — üzerinde çalışılabilir (sıralı modda kilitli olabilir).</summary>
    Open = 0,

    /// <summary>Tamamlandı.</summary>
    Completed = 1,

    /// <summary>İptal — ilerleme yüzdesi paydasından düşer, sıra kilidini açar.</summary>
    Cancelled = 2,
}
