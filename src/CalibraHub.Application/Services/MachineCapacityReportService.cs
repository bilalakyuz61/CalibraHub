using System.Globalization;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services;

/// <summary>
/// Makine Planlama — Kapasite / Yük Raporu (backend, 2026-08-22). Bkz. <c>CapacityLoadContracts.cs</c>
/// XML doc'u (kilitli API sözleşmesi) + <c>IMachineCapacityReportService</c> XML doc'u.
///
/// <para><b>Zaman dilimi yaklaşımı:</b> Faz 3 motoruyla (<c>MachineAutoScheduleService</c>) AYNI
/// desen — <see cref="TimeZoneInfo.Local"/> ile yerelde hesaplanır, sonuç UTC'ye çevrilir. Kova
/// (gün/hafta) sınırları YEREL takvimden türetilir; bloklar UTC tutulur, o güne/haftaya denk gelen
/// kısmı yerelde kırpılır.</para>
///
/// <para><b>Kapasite hesabı</b> (yerel gün başına, sonra kovaya toplanır): resmi tatilse 0; değilse
/// makinenin o haftanın-günü için <c>MachineWorkWindow</c> (varsayılan Vardiya Senaryosu üzerinden
/// türetilmiş — <see cref="IMachineCalendarRepository.ListWorkWindowsAsync"/> ile Gantt gölgelemesiyle
/// AYNI kaynak) dakika toplamı; makinenin HİÇ penceresi yoksa (hiçbir gün) 7/24=1440 (Faz 3 motor
/// paritesi — <c>has247</c>). Sonra o güne denk gelen Bakım(3)/Duruş(4) bloklarının yerelde kırpılmış
/// dakikaları düşülür (0'ın altına inmez).</para>
///
/// <para><b>Yük hesabı:</b> Üretim(1)+Hazırlık(2) bloklarının o güne denk gelen (yerelde kırpılmış)
/// dakikaları. <c>UtilizationPct</c> yalnız kapasite&gt;0 iken hesaplanır (aksi halde null — frontend
/// "kapasite yok" olarak yorumlar).</para>
///
/// <para><b>Kova hizalama kararı (küçük karar, 2026-08-22):</b> <c>bucket=week</c> istendiğinde
/// hafta ISO Pazartesi'ye hizalanır — istenen aralığın ilk/son günü hafta ortasına denk gelse bile
/// kova o haftanın TAMAMINI (Pazartesi-Pazar) kapsar; bu, <c>bucket=day</c>'in de "from/to yerel gün
/// sınırlarını temsil eder" ilkesiyle tutarlıdır (frontend zaten hafta-hizalı from/to göndermesi
/// beklenir, ama backend güvenlik için kendi hizalamasını yapar).</para>
///
/// <para><b>Sağlamlık sınırı:</b> raporlanan yerel gün sayısı 400'ü (yaklaşık 13 ay) aşarsa
/// <see cref="ArgumentException"/> fırlatılır — kaza eseri çok geniş bir aralık isteği sunucuyu
/// yormasın diye.</para>
/// </summary>
public sealed class MachineCapacityReportService : IMachineCapacityReportService
{
    private const int MaxLocalDays = 400;
    private static readonly CultureInfo TrTr = CultureInfo.GetCultureInfo("tr-TR");

    private readonly IMachineCalendarRepository _calendar;
    private readonly IMachineScheduleRepository _schedule;

    public MachineCapacityReportService(IMachineCalendarRepository calendar, IMachineScheduleRepository schedule)
    {
        _calendar = calendar;
        _schedule = schedule;
    }

    public async Task<CapacityLoadResultDto> GetCapacityLoadAsync(DateTime fromUtc, DateTime toUtc, string bucket, CancellationToken ct)
    {
        var normalizedBucket = (bucket ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedBucket != "day" && normalizedBucket != "week")
            throw new ArgumentException("bucket yalnız 'day' veya 'week' olabilir.");

        var localFrom = UtcToLocal(fromUtc).Date;
        var localToRaw = UtcToLocal(toUtc).Date;
        var localToExclusive = localToRaw > localFrom ? localToRaw : localFrom.AddDays(1);

        // Rapor kapsamı — week kovasında ISO haftaya hizalanır (bkz. sınıf XML doc'u).
        var reportStart = normalizedBucket == "week" ? AlignToMonday(localFrom) : localFrom;
        var reportEndExclusive = localToExclusive;
        if (normalizedBucket == "week")
        {
            var lastWeekStart = AlignToMonday(localToExclusive.AddDays(-1));
            reportEndExclusive = lastWeekStart.AddDays(7);
        }

        var totalDays = (reportEndExclusive - reportStart).Days;
        if (totalDays > MaxLocalDays)
            throw new ArgumentException($"İstenen aralık çok geniş (maks {MaxLocalDays} gün).");
        if (totalDays <= 0)
            reportEndExclusive = reportStart.AddDays(1);

        var machines = await _calendar.ListActiveMachinesAsync(ct);
        var windowsRaw = await _calendar.ListWorkWindowsAsync(ct, scenarioId: null);
        var holidaysRaw = await _calendar.ListHolidaysAsync(ct);

        var blocksFromUtc = LocalToUtc(reportStart);
        var blocksToUtc = LocalToUtc(reportEndExclusive);
        var blocksRaw = await _schedule.ListBlocksForCapacityReportAsync(blocksFromUtc, blocksToUtc, ct);

        var windowsByMachine = new Dictionary<int, Dictionary<byte, List<(short Start, short End)>>>();
        var machinesWithWindows = new HashSet<int>();
        foreach (var w in windowsRaw)
        {
            machinesWithWindows.Add(w.MachineId);
            if (!windowsByMachine.TryGetValue(w.MachineId, out var byDay))
                windowsByMachine[w.MachineId] = byDay = new Dictionary<byte, List<(short, short)>>();
            if (!byDay.TryGetValue(w.DayOfWeek, out var list))
                byDay[w.DayOfWeek] = list = new List<(short, short)>();
            list.Add((w.StartMinute, w.EndMinute));
        }

        var holidayDates = new HashSet<DateTime>();
        foreach (var h in holidaysRaw)
        {
            if (DateTime.TryParseExact(h.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                holidayDates.Add(d.Date);
        }

        var blocksByMachine = new Dictionary<int, List<(DateTime StartLocal, DateTime EndLocal, byte BlockType)>>();
        foreach (var b in blocksRaw)
        {
            if (!blocksByMachine.TryGetValue(b.MachineId, out var list))
                blocksByMachine[b.MachineId] = list = new List<(DateTime, DateTime, byte)>();
            list.Add((UtcToLocal(b.StartUtc), UtcToLocal(b.EndUtc), b.BlockType));
        }

        // ── Kova iskeleti (gün listeleri + dış sözleşme) ────────────────────────────
        var bucketDefs = new List<(string Key, string Label, DateTime StartLocal, DateTime EndLocalExclusive, List<DateTime> Days)>();
        if (normalizedBucket == "day")
        {
            for (var d = reportStart; d < reportEndExclusive; d = d.AddDays(1))
            {
                bucketDefs.Add((d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    d.ToString("d MMM", TrTr), d, d.AddDays(1), new List<DateTime> { d }));
            }
        }
        else
        {
            for (var weekStart = reportStart; weekStart < reportEndExclusive; weekStart = weekStart.AddDays(7))
            {
                var days = new List<DateTime>(7);
                for (var i = 0; i < 7; i++) days.Add(weekStart.AddDays(i));
                bucketDefs.Add((weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    weekStart.ToString("d MMM", TrTr), weekStart, weekStart.AddDays(7), days));
            }
        }

        var buckets = bucketDefs
            .Select(b => new CapacityLoadBucketDto(b.Key, b.Label, LocalToUtc(b.StartLocal), LocalToUtc(b.EndLocalExclusive)))
            .ToList();

        var machineResults = new List<CapacityLoadMachineDto>();
        foreach (var m in machines)
        {
            var hasWindows = machinesWithWindows.Contains(m.Id);
            blocksByMachine.TryGetValue(m.Id, out var machineBlocks);
            windowsByMachine.TryGetValue(m.Id, out var machineWindows);

            var cells = new List<CapacityLoadCellDto>(bucketDefs.Count);
            foreach (var b in bucketDefs)
            {
                var capacity = 0;
                var load = 0;
                foreach (var day in b.Days)
                {
                    capacity += DayCapacityMinutes(day, hasWindows, machineWindows, machineBlocks, holidayDates);
                    load += DayLoadMinutes(day, machineBlocks);
                }

                int? utilizationPct = capacity > 0 ? (int)Math.Round(load / (decimal)capacity * 100m, MidpointRounding.AwayFromZero) : null;
                var overload = Math.Max(0, load - capacity);
                cells.Add(new CapacityLoadCellDto(b.Key, capacity, load, utilizationPct, overload));
            }

            machineResults.Add(new CapacityLoadMachineDto(m.Id, m.Name ?? m.Code, cells));
        }

        return new CapacityLoadResultDto(normalizedBucket, buckets, machineResults);
    }

    private static int DayCapacityMinutes(
        DateTime day,
        bool hasWindows,
        Dictionary<byte, List<(short Start, short End)>>? windowsByDay,
        List<(DateTime StartLocal, DateTime EndLocal, byte BlockType)>? blocks,
        HashSet<DateTime> holidayDates)
    {
        if (holidayDates.Contains(day.Date)) return 0;

        int minutes;
        if (!hasWindows)
        {
            minutes = 1440; // makinenin hiç tanımlı penceresi yok → 7/24 (Faz 3 motor paritesi)
        }
        else
        {
            minutes = 0;
            if (windowsByDay is not null && windowsByDay.TryGetValue((byte)day.DayOfWeek, out var todays))
            {
                foreach (var w in todays)
                    minutes += w.End - w.Start;
            }
        }

        if (blocks is not null && minutes > 0)
        {
            var dayStart = day.Date;
            var dayEnd = dayStart.AddDays(1);
            foreach (var blk in blocks)
            {
                if (blk.BlockType != 3 && blk.BlockType != 4) continue; // Bakım(3) / Duruş(4)
                var clipStart = blk.StartLocal < dayStart ? dayStart : blk.StartLocal;
                var clipEnd = blk.EndLocal > dayEnd ? dayEnd : blk.EndLocal;
                if (clipEnd > clipStart)
                    minutes -= (int)Math.Round((clipEnd - clipStart).TotalMinutes);
            }
        }

        return Math.Max(0, minutes);
    }

    private static int DayLoadMinutes(DateTime day, List<(DateTime StartLocal, DateTime EndLocal, byte BlockType)>? blocks)
    {
        if (blocks is null) return 0;

        var minutes = 0;
        var dayStart = day.Date;
        var dayEnd = dayStart.AddDays(1);
        foreach (var blk in blocks)
        {
            if (blk.BlockType != 1 && blk.BlockType != 2) continue; // Üretim(1) / Hazırlık(2)
            var clipStart = blk.StartLocal < dayStart ? dayStart : blk.StartLocal;
            var clipEnd = blk.EndLocal > dayEnd ? dayEnd : blk.EndLocal;
            if (clipEnd > clipStart)
                minutes += (int)Math.Round((clipEnd - clipStart).TotalMinutes);
        }
        return minutes;
    }

    private static DateTime AlignToMonday(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // Pazartesi=0 ... Pazar=6
        return date.AddDays(-offset);
    }

    private static DateTime UtcToLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local);

    private static DateTime LocalToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // DST "spring forward" boşluğu — bir sonraki geçerli ana (+1 saat) kaydır (Faz 3 motoruyla aynı guard).
        if (TimeZoneInfo.Local.IsInvalidTime(unspecified))
            unspecified = unspecified.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
    }
}
