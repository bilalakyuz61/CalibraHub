namespace CalibraHub.Domain.Enums;

/// <summary>
/// Gorev zamanlama ifadesinin turunu belirler. Scheduler, ScheduleExpression'i
/// bu tipe gore parse ederek NextRunAt hesaplar.
/// </summary>
public enum ScheduleType
{
    /// <summary>
    /// Belirli araliklarla calisir. Expression: saniye cinsinden integer ("60", "300", "3600").
    /// </summary>
    Interval = 0,

    /// <summary>
    /// Standart cron ifadesi. Expression: "0 9 * * *" (cron 5 alan). Cronos kutuphanesi veya
    /// basit parser ile degerlendirilir.
    /// </summary>
    Cron = 1,

    /// <summary>
    /// Her gun belirli saat. Expression: "HH:mm" ("09:00", "23:30").
    /// </summary>
    DailyAt = 2,

    /// <summary>
    /// Bir kez, belirli tarih. Expression: ISO 8601 ("2026-05-01T09:00:00").
    /// Calistiktan sonra IsEnabled=false yapilir.
    /// </summary>
    Once = 3,

    /// <summary>
    /// Sadece manuel tetiklenir, otomatik calismaz (Expression ignore edilir).
    /// </summary>
    Manual = 4,

    /// <summary>
    /// Haftanin belirli gunleri, belirli saatte. Expression: "HH:mm|d1,d2,..."
    /// gunler 0=Pazar..6=Cumartesi (DayOfWeek). Ornek: "09:00|1,3,5" = pzt/cars/cuma 09:00.
    /// </summary>
    WeeklyOnDays = 5,

    /// <summary>
    /// Ayin belirli gunleri, belirli saatte. Expression: "HH:mm|d1,d2,..."
    /// gunler 1..31. Ornek: "10:00|1,15" = her ayin 1 ve 15'inde 10:00.
    /// </summary>
    MonthlyOnDays = 6,
}
