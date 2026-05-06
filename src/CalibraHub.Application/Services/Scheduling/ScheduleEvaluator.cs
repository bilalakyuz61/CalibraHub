using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services.Scheduling;

/// <summary>
/// ScheduleType + ScheduleExpression'a gore bir sonraki calistirma zamanini (UTC) hesaplar.
/// Cron desteklenmesi icin externally Cronos gibi bir kutuphane eklenebilir; simdilik
/// basit implementation — Interval, DailyAt, Once, Manual.
/// </summary>
public static class ScheduleEvaluator
{
    /// <summary>
    /// Verilen kayit icin bir sonraki calistirma zamanini hesaplar.
    /// fromUtc genellikle GETUTCDATE — ama hata sonrasi calisma icin farkli baslangic verilebilir.
    /// </summary>
    public static DateTime? ComputeNextRun(ScheduledTask task, DateTime fromUtc)
    {
        if (!task.IsEnabled) return null;
        var expr = task.ScheduleExpression?.Trim() ?? string.Empty;

        return task.ScheduleType switch
        {
            ScheduleType.Interval      => ComputeInterval(expr, fromUtc),
            ScheduleType.DailyAt       => ComputeDailyAt(expr, fromUtc),
            ScheduleType.Once          => ComputeOnce(expr, fromUtc),
            ScheduleType.Cron          => ComputeCron(expr, fromUtc),
            ScheduleType.WeeklyOnDays  => ComputeWeeklyOnDays(expr, fromUtc),
            ScheduleType.MonthlyOnDays => ComputeMonthlyOnDays(expr, fromUtc),
            ScheduleType.Manual        => null,
            _                          => null,
        };
    }

    /// <summary>"HH:mm|d1,d2,..." formati. d=0..6 (DayOfWeek). Yerel saat olarak hesaplanir.</summary>
    private static DateTime? ComputeWeeklyOnDays(string expr, DateTime fromUtc)
    {
        var (todOk, tod, days) = ParseTimeAndIntList(expr);
        if (!todOk || days.Count == 0) return null;

        var nowLocal = fromUtc.ToLocalTime();
        // 14 gun forward arar; bugun de dahil
        for (var i = 0; i < 14; i++)
        {
            var candidate = nowLocal.Date.AddDays(i).Add(tod);
            if (candidate <= nowLocal) continue;
            if (days.Contains((int)candidate.DayOfWeek))
            {
                return candidate.ToUniversalTime();
            }
        }
        return null;
    }

    /// <summary>"HH:mm|d1,d2,..." formati. d=1..31. Bu ayda eslesme yoksa sonraki aylara ilerler.</summary>
    private static DateTime? ComputeMonthlyOnDays(string expr, DateTime fromUtc)
    {
        var (todOk, tod, days) = ParseTimeAndIntList(expr);
        if (!todOk || days.Count == 0) return null;

        var nowLocal = fromUtc.ToLocalTime();
        // 12 ay ileriye kadar bak (yine de bos donerse null)
        var probe = new DateTime(nowLocal.Year, nowLocal.Month, 1, 0, 0, 0, DateTimeKind.Local);
        for (var monthOffset = 0; monthOffset < 12; monthOffset++)
        {
            var month = probe.AddMonths(monthOffset);
            var lastDay = DateTime.DaysInMonth(month.Year, month.Month);
            foreach (var d in days.OrderBy(x => x))
            {
                if (d < 1 || d > lastDay) continue;
                var candidate = new DateTime(month.Year, month.Month, d, 0, 0, 0, DateTimeKind.Local).Add(tod);
                if (candidate > nowLocal) return candidate.ToUniversalTime();
            }
        }
        return null;
    }

    private static (bool ok, TimeSpan tod, HashSet<int> days) ParseTimeAndIntList(string expr)
    {
        var parts = expr.Split('|', 2);
        if (parts.Length != 2) return (false, TimeSpan.Zero, new HashSet<int>());
        if (!TimeSpan.TryParse(parts[0].Trim(), out var tod)) return (false, TimeSpan.Zero, new HashSet<int>());
        var set = new HashSet<int>();
        foreach (var token in parts[1].Split(','))
        {
            if (int.TryParse(token.Trim(), out var v)) set.Add(v);
        }
        return (set.Count > 0, tod, set);
    }

    private static DateTime? ComputeInterval(string expr, DateTime fromUtc)
    {
        if (int.TryParse(expr, out var seconds) && seconds > 0)
        {
            return fromUtc.AddSeconds(seconds);
        }
        return null;
    }

    private static DateTime? ComputeDailyAt(string expr, DateTime fromUtc)
    {
        if (!TimeSpan.TryParse(expr, out var tod)) return null;

        // Ifade yerel saat (HTML <input type="time"> HH:mm). Hesabi yerel saatte yap,
        // sonuclanan zamani UTC'ye cevirerek dondur — next_run_at UTC olarak saklanir.
        var nowLocal   = fromUtc.ToLocalTime();
        var todayAtTod = nowLocal.Date.Add(tod);
        var nextLocal  = nowLocal < todayAtTod ? todayAtTod : todayAtTod.AddDays(1);
        return nextLocal.ToUniversalTime();
    }

    private static DateTime? ComputeOnce(string expr, DateTime fromUtc)
    {
        if (!DateTime.TryParse(expr, out var when)) return null;

        // Ifade HTML datetime-local (yerel saat). Kind=Unspecified geldiginde Local kabul
        // edip UTC'ye cevir. Kind=Local ise ToUniversalTime yeter; Kind=Utc ise dokunma.
        when = when.Kind switch
        {
            DateTimeKind.Local       => when.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(when, DateTimeKind.Local).ToUniversalTime(),
            _                        => when,
        };
        return when > fromUtc ? when : null; // gecmis → artik calistirma
    }

    /// <summary>
    /// Basit cron parser — yalnizca dakika/saat/gun field'larini destekler.
    /// Tam cron icin Cronos NuGet paketi eklenebilir; simdilik null donersa scheduler
    /// bir saat sonraya set eder (cron gorevleri calismaz).
    /// </summary>
    private static DateTime? ComputeCron(string expr, DateTime fromUtc)
    {
        // TODO: Cronos paketi eklendiginde burayi gercek cron parser ile degistir.
        // Simdilik 1 saat sonrayi donerek gorevin asla surfes'ten kaybolmasini engelle.
        return fromUtc.AddHours(1);
    }
}
