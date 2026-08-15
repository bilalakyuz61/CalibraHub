namespace CalibraHub.Domain.Enums;

/// <summary>
/// Aktarım öncesi/sonrası prosedürün çalışacağı veritabanı.
/// </summary>
public enum DataImportProcedureTarget
{
    /// <summary>CalibraHub'ın kendi şirket DB'si (varsayılan — harici kaynağa yazılmaz).</summary>
    Calibra = 0,

    /// <summary>
    /// Harici kaynak DB. DİKKAT: kaynağa YAZMA yolu budur (örn. okunan satırları
    /// "aktarıldı" işaretlemek). Yalnız açıkça seçilirse çalışır; okuma yolu
    /// (<c>IExternalDbReader</c>) her koşulda salt-okunur kalır.
    /// </summary>
    Source = 1,
}

/// <summary>
/// Prosedür hatalarının aktarımı nasıl etkileyeceği — iş tanımı başına seçilir.
/// </summary>
public enum DataImportErrorBehavior
{
    /// <summary>
    /// Ön prosedür hatası aktarımı DURDURUR (veriyi o hazırlar, hatalıysa aktarım anlamsız);
    /// son prosedür hatası yalnızca uyarıdır — yazılmış kayıtlar geri alınmaz,
    /// çalıştırma "uyarılı başarılı" işaretlenir.
    /// </summary>
    Lenient = 0,

    /// <summary>
    /// Katı mod: ön VEYA son prosedür hatası çalıştırmayı "başarısız" işaretler
    /// (son prosedür patladığında kayıtlar yazılmış olsa bile).
    /// </summary>
    Strict = 1,
}

/// <summary>Aktarımı ne tetikledi.</summary>
public enum DataImportTriggerType
{
    /// <summary>Kullanıcı ekrandan çalıştırdı.</summary>
    Manual = 0,

    /// <summary>Zamanlanmış görev (ScheduledTask).</summary>
    Cron = 1,
}

/// <summary>Aktarım çalıştırmasının sonucu.</summary>
public enum DataImportRunStatus
{
    Success = 0,
    Failed = 1,

    /// <summary>Kayıtlar yazıldı ama son prosedür hata verdi (Lenient mod).</summary>
    SucceededWithWarnings = 2,
}
