namespace CalibraHub.Domain.Enums;

/// <summary>Bir run kaydinin hangi sekilde tetiklendigini belirtir.</summary>
public enum RunTrigger
{
    /// <summary>Schedule dolayisi ile otomatik tetiklendi.</summary>
    Schedule = 0,

    /// <summary>Kullanici "Simdi Calistir" tusuna bastigi icin tetiklendi.</summary>
    Manual = 1,

    /// <summary>Sistem tarafindan iceriden tetiklendi (dependent task, vs).</summary>
    System = 2,
}
