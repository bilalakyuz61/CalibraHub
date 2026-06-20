using System.Globalization;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services.Calendar;

/// <summary>
/// Türkiye resmi tatil + dini bayram tarihleri. Hardcoded — hicri takvim hesaplaması
/// astronomik karmaşıklığı + Diyanet'in son anda revize edebilmesi nedeniyle yıl bazlı
/// elle tutulur tablo en güvenlisi. Yeni yıllar 2032 sonrası gelmeden eklenmeli.
///
/// CalendarEventDto.Source = "holiday"; Color = "rose" (kırmızı).
/// Çoklu gün bayramlar (Ramazan/Kurban) her gün ayrı event olarak döner — UI tek satır
/// "Ramazan Bayramı (2. Gün)" gibi gösterir.
/// </summary>
public static class TurkishHolidayProvider
{
    /// <summary>Sabit Miladi tarihli ulusal/resmi tatiller (MM-DD → ad).</summary>
    private static readonly (int Month, int Day, string Name)[] FixedHolidays =
    {
        (1,  1,  "Yılbaşı"),
        (4,  23, "Ulusal Egemenlik ve Çocuk Bayramı"),
        (5,  1,  "Emek ve Dayanışma Günü"),
        (5,  19, "Atatürk'ü Anma, Gençlik ve Spor Bayramı"),
        (7,  15, "Demokrasi ve Milli Birlik Günü"),
        (8,  30, "Zafer Bayramı"),
        (10, 29, "Cumhuriyet Bayramı"),
    };

    /// <summary>
    /// Dini bayramlar — Diyanet ilan tarihleri (2025-2032). Her bayram başlangıç-bitiş.
    /// Sözlük key = yıl, value = (Ramazan Başlangıç, Ramazan Gün Sayısı, Kurban Başlangıç, Kurban Gün Sayısı).
    /// </summary>
    private static readonly Dictionary<int, (DateTime RamazanStart, int RamazanDays, DateTime KurbanStart, int KurbanDays)> ReligiousHolidays = new()
    {
        // Türkiye Diyanet İşleri Başkanlığı resmi takvimi
        [2025] = (new DateTime(2025, 3, 30),  3, new DateTime(2025, 6, 6),  4),
        [2026] = (new DateTime(2026, 3, 20),  3, new DateTime(2026, 5, 27), 4),
        [2027] = (new DateTime(2027, 3, 9),   3, new DateTime(2027, 5, 16), 4),
        [2028] = (new DateTime(2028, 2, 26),  3, new DateTime(2028, 5, 5),  4),
        [2029] = (new DateTime(2029, 2, 14),  3, new DateTime(2029, 4, 24), 4),
        [2030] = (new DateTime(2030, 2, 4),   3, new DateTime(2030, 4, 13), 4),
        [2031] = (new DateTime(2031, 1, 24),  3, new DateTime(2031, 4, 2),  4),
        [2032] = (new DateTime(2032, 1, 13),  3, new DateTime(2032, 3, 21), 4),
    };

    /// <summary>
    /// Verilen tarih aralığı (YYYY-MM-DD start, end dahil) içine düşen
    /// resmi + dini tatilleri CalendarEventDto listesi olarak döner. Negatif id'ler
    /// (-1'den geriye doğru) — DB'deki personal event id'leriyle çakışmaz.
    /// </summary>
    public static IEnumerable<CalendarEventDto> GetForRange(string startStr, string endStr)
    {
        if (!TryParseDate(startStr, out var rangeStart)) yield break;
        if (!TryParseDate(endStr, out var rangeEnd)) yield break;
        if (rangeEnd < rangeStart) yield break;

        var idSeed = -1;

        // Sabit resmi tatiller — range içindeki her yıl için
        for (var year = rangeStart.Year; year <= rangeEnd.Year; year++)
        {
            foreach (var (month, day, name) in FixedHolidays)
            {
                var d = SafeDate(year, month, day);
                if (d is null) continue;
                if (d.Value < rangeStart || d.Value > rangeEnd) continue;
                yield return MakeHoliday(idSeed--, name, d.Value);
            }

            // Dini bayramlar
            if (!ReligiousHolidays.TryGetValue(year, out var rel)) continue;

            for (var i = 0; i < rel.RamazanDays; i++)
            {
                var d = rel.RamazanStart.AddDays(i);
                if (d < rangeStart || d > rangeEnd) continue;
                var label = rel.RamazanDays > 1
                    ? $"Ramazan Bayramı ({i + 1}. Gün)"
                    : "Ramazan Bayramı";
                yield return MakeHoliday(idSeed--, label, d);
            }
            for (var i = 0; i < rel.KurbanDays; i++)
            {
                var d = rel.KurbanStart.AddDays(i);
                if (d < rangeStart || d > rangeEnd) continue;
                var label = rel.KurbanDays > 1
                    ? $"Kurban Bayramı ({i + 1}. Gün)"
                    : "Kurban Bayramı";
                yield return MakeHoliday(idSeed--, label, d);
            }
        }
    }

    private static CalendarEventDto MakeHoliday(int id, string title, DateTime date)
    {
        var dateStr = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new CalendarEventDto(
            Id:          id,
            Title:       title,
            Description: "Resmi tatil",
            StartDate:   dateStr,
            EndDate:     dateStr,
            IsAllDay:    true,
            StartTime:   null,
            EndTime:     null,
            Color:       "rose",
            Source:      "holiday");
    }

    private static bool TryParseDate(string? s, out DateTime d)
        => DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out d);

    private static DateTime? SafeDate(int y, int m, int d)
    {
        try { return new DateTime(y, m, d); }
        catch { return null; }
    }
}
