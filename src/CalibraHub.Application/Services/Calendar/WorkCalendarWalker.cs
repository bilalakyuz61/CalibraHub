using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services.Calendar;

/// <summary>
/// Çalışma takvimi üzerinde zaman yürütücü — MRP'nin GERİYE doğru tarihlemesi için (2026-08-29).
///
/// <para><b>Neden takvim farkında:</b> düz <c>AddMinutes(-x)</c> hafta sonunu ve tatili işgünü
/// sayar; MRP'nin ürettiği başlangıç tarihi, aynı işi ileri yönde yerleştiren Makine Planlama
/// (<c>MachineAutoScheduleService.PlaceOnMachine</c>) ile tutmaz. Aynı takvim kaynağı
/// (<c>MachineWorkWindow</c> + <c>CompanyHoliday</c>) burada da kullanılır.</para>
///
/// <para><b>Pencere kaynağı:</b> MRP'de henüz makine atanmamıştır (emir Planned doğar), bu yüzden
/// makine-özel pencere kullanılamaz. Tüm aktif makinelerin pencerelerinin BİRLEŞİMİ alınır —
/// "fabrika hangi saatlerde çalışıyor" sorusunun cevabı. Kaba planlama için doğru soyutlama;
/// makine bazlı ince yerleştirme zaten Released aşamasında çizelgeleme yapar.</para>
///
/// <para>Gün konvansiyonu <c>Sunday=0 … Saturday=6</c> (CalibraHub kanonu, bkz.
/// <c>MachineAutoScheduleService</c>). Tatiller tam gün kapalı sayılır.</para>
/// </summary>
public sealed class WorkCalendarWalker
{
    /// <summary>Gün içi çalışma aralıkları: gün → [(başlangıç dk, bitiş dk)] (birleştirilmiş, sıralı).</summary>
    private readonly Dictionary<byte, List<(short Start, short End)>> _byDay;
    private readonly HashSet<DateTime> _holidays;

    /// <summary>Takvim hiç tanımlı değilse (pencere yok) yürüyüş yapılamaz — çağıran bunu bilmeli.</summary>
    public bool HasCalendar { get; }

    /// <summary>Bir haftada toplam kaç dakika çalışılıyor (sonsuz döngü koruması için).</summary>
    private readonly int _weeklyMinutes;

    public WorkCalendarWalker(
        IReadOnlyList<MachineWorkWindowDto> windows,
        IReadOnlyList<HolidayDto> holidays)
    {
        _byDay = [];
        foreach (var w in windows ?? [])
        {
            if (w.EndMinute <= w.StartMinute) continue;   // gece aşan/bozuk pencere: kapsam dışı
            if (!_byDay.TryGetValue(w.DayOfWeek, out var list))
                _byDay[w.DayOfWeek] = list = [];
            list.Add((w.StartMinute, w.EndMinute));
        }

        // Makineler arası çakışan pencereler BİRLEŞTİRİLİR — aynı saat iki makinede açıksa
        // fabrika o saatte bir kez çalışıyordur, iki kez değil.
        foreach (var key in _byDay.Keys.ToList())
        {
            var merged = new List<(short Start, short End)>();
            foreach (var iv in _byDay[key].OrderBy(x => x.Start))
            {
                if (merged.Count > 0 && iv.Start <= merged[^1].End)
                {
                    if (iv.End > merged[^1].End) merged[^1] = (merged[^1].Start, iv.End);
                }
                else merged.Add(iv);
            }
            _byDay[key] = merged;
        }

        _holidays = (holidays ?? [])
            .Select(h => DateTime.TryParse(h.Date, out var d) ? d.Date : (DateTime?)null)
            .Where(d => d.HasValue).Select(d => d!.Value)
            .ToHashSet();

        _weeklyMinutes = _byDay.Values.Sum(list => list.Sum(iv => iv.End - iv.Start));
        HasCalendar = _weeklyMinutes > 0;
    }

    private bool IsWorkingDay(DateTime day) =>
        !_holidays.Contains(day.Date) && _byDay.ContainsKey((byte)day.DayOfWeek);

    /// <summary>
    /// <paramref name="end"/> anından GERİYE doğru <paramref name="minutes"/> kadar ÇALIŞMA
    /// süresi sayarak başlangıç anını bulur. Kapalı saatler/tatiller atlanır (sayılmaz).
    ///
    /// <para>Takvim tanımlı değilse veya süre 0 ise <paramref name="end"/> aynen döner —
    /// sessizce yanlış bir tarih uydurmaktansa "kaydırma yapılamadı" demek doğrudur; çağıran
    /// bu durumu kullanıcıya açıklar.</para>
    /// </summary>
    public DateTime WalkBackward(DateTime end, decimal minutes)
    {
        if (!HasCalendar || minutes <= 0m) return end;

        var remaining = (double)minutes;

        // Durum (gün, o günde sayılabilecek EN GEÇ dakika) çifti olarak taşınır. Tek bir
        // DateTime "imleç" kullanmak hataya açık: önceki güne geçmek için gün sonunu
        // ifade etmenin doğal bir yolu yok (gün-1'in 24:00'ü = bugünün 00:00'ı → aynı güne
        // geri dönülür ve döngü ilerlemez).
        var day = end.Date;
        var limit = (int)(end - day).TotalMinutes;   // ilk gün: bitiş anına kadar

        // Sonsuz döngü koruması: gereken gün sayısı + geniş pay, en fazla ~10 yıl.
        var perDay = Math.Max(1.0, _weeklyMinutes / 7.0);
        var maxDays = (int)Math.Min(3650, Math.Ceiling(remaining / perDay) + 400);

        for (var guard = 0; guard < maxDays; guard++)
        {
            if (IsWorkingDay(day))
            {
                // O günün açık aralıkları, GEÇ saatten erkene doğru tüketilir.
                var intervals = _byDay[(byte)day.DayOfWeek];
                for (var i = intervals.Count - 1; i >= 0; i--)
                {
                    var (s, e) = intervals[i];
                    var segEnd = Math.Min((int)e, limit);
                    if (segEnd <= s) continue;              // aralık limitin ilerisinde

                    var segLen = segEnd - s;
                    if (segLen >= remaining) return day.AddMinutes(segEnd - remaining);
                    remaining -= segLen;
                }
            }

            day = day.AddDays(-1);
            limit = 24 * 60;                                // önceki günler tamamen taranır
            if (remaining <= 0.0001) return day.AddDays(1); // güvenlik: zaten yukarıda dönülür
        }

        // Süre takvime sığmadı (aşırı uzun üretim / çok seyrek takvim): ulaşılan en erken güne
        // düşülür. Sessizce "bugün" demek, gerçekte imkânsız bir planı mümkün göstermek olurdu.
        return day;
    }
}
