using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

/// <summary>
/// Makine Planlama Faz 3 (2026-08-05) — forward, sonlu-kapasite otomatik çizelgeleme motoru.
/// Bkz. <c>IMachineAutoScheduleService</c> XML doc'u + backend raporu (kullanıcı kararları:
/// sıralama Priority↓/PlannedEndDate↑, çalışma penceresi aşımında bölme, önce önizleme sonra
/// uygula, aday listesinden hariç tutma).
///
/// <para><b>Zaman dilimi yaklaşımı:</b> <see cref="MachineWorkWindow"/>/<see cref="CompanyHoliday"/>
/// YEREL duvar-saati tutar (Faz 2 frontend paritesi). Motor tüm yerleştirme hesabını
/// <see cref="TimeZoneInfo.Local"/> ile yerelde yapar, sonucu UTC'ye çevirir. DST "spring forward"
/// boşluğuna denk gelen bir an bir sonraki geçerli ana (+1 saat) kaydırılır — "fall back" belirsiz
/// aralığı ConvertTimeToUtc varsayılan (standart zaman) davranışına bırakılır.</para>
///
/// <para><b>Setup yerleştirme kararı:</b> Hazırlık (setup) süresi üretim süresine ayrı bir
/// <see cref="PlaceOnMachine"/> çağrısıyla DEĞİL, TEK çağrıda (setup+üretim toplamı) yerleştirilir;
/// dönen kronolojik segment listesinin İLK <c>setupMinutes</c> dakikalık kısmı Setup, kalanı
/// Production olarak etiketlenir (bir segment sınırda kesişirse ikiye bölünür). Bu, setup'ın
/// üretimden hemen önce ve boşluksuz gelmesini doğal olarak garanti eder ve iki ayrı arama
/// çağrısının senkronizasyon riskini ortadan kaldırır (basit + doğru — CLAUDE.md karar notu).</para>
///
/// <para><b>Determinizm:</b> Motor <c>DateTime.Now</c>/<c>Random</c> KULLANMAZ — tek zaman
/// referansı çağıranın verdiği <c>fromUtc</c> anchor'ıdır. Preview ve Apply aynı girdiyle aynı
/// sonucu üretir (Apply, Preview'un client'a döndürdüğü koordinatlara güvenmez — kendi yeniden
/// hesaplar).</para>
/// </summary>
public sealed class MachineAutoScheduleService : IMachineAutoScheduleService
{
    private const int MaxPlacementIterations = 500;

    private readonly IMachineAutoScheduleRepository _repo;
    private readonly IMachineCalendarRepository _calendar;
    private readonly IOperationMachineTimeService _durationResolver;

    public MachineAutoScheduleService(
        IMachineAutoScheduleRepository repo,
        IMachineCalendarRepository calendar,
        IOperationMachineTimeService durationResolver)
    {
        _repo = repo;
        _calendar = calendar;
        _durationResolver = durationResolver;
    }

    public Task<IReadOnlyList<AutoScheduleCandidateWorkOrderDto>> GetCandidatesAsync(CancellationToken ct)
        => _repo.GetCandidateWorkOrdersAsync(ct);

    public async Task<AutoSchedulePreviewResultDto> PreviewAsync(
        IReadOnlyList<int> includedWorkOrderIds, DateTime fromUtc, int? scenarioId, CancellationToken ct)
    {
        var (segments, unplaceable) = await RunEngineAsync(includedWorkOrderIds, fromUtc, scenarioId, ct);
        var proposals = segments.Select(s => new AutoScheduleProposalDto(
            TempId: $"{s.WorkOrderOperationId}:{s.BlockType}:{s.SegmentIndex}",
            MachineId: s.MachineId,
            MachineName: s.MachineName,
            WorkOrderOperationId: s.WorkOrderOperationId,
            WorkOrderNo: s.WorkOrderNo,
            ItemName: s.ItemName,
            OperationName: s.OperationName,
            BlockType: s.BlockType,
            StartUtc: s.StartUtc,
            EndUtc: s.EndUtc,
            SegmentIndex: s.SegmentIndex,
            SegmentCount: s.SegmentCount)).ToList();
        return new AutoSchedulePreviewResultDto(proposals, unplaceable);
    }

    public async Task<AutoScheduleApplyResultDto> ApplyAsync(
        IReadOnlyList<int> includedWorkOrderIds, DateTime fromUtc, int? scenarioId, int? userId, CancellationToken ct)
    {
        var (segments, unplaceable) = await RunEngineAsync(includedWorkOrderIds, fromUtc, scenarioId, ct);

        var operations = segments
            .GroupBy(s => s.WorkOrderOperationId)
            .Select(g => new PlannedOperationBlocksDto(
                g.Key,
                g.OrderBy(s => s.StartUtc)
                 .Select(s => new PlannedSegmentDto(s.MachineId, s.StartUtc, s.EndUtc, s.BlockType))
                 .ToList()))
            .ToList();

        var created = operations.Count == 0 ? 0 : await _repo.InsertBlocksAsync(operations, userId, ct);
        return new AutoScheduleApplyResultDto(created, unplaceable.Count);
    }

    private sealed record EngineSegment(
        int WorkOrderOperationId, string? WorkOrderNo, string? ItemName, string? OperationName,
        int MachineId, string? MachineName, byte BlockType, DateTime StartUtc, DateTime EndUtc,
        int SegmentIndex, int SegmentCount);

    /// <summary>Motor çekirdeği. Preview ve Apply tarafından AYNI şekilde çağrılır (bkz. sınıf
    /// XML doc'u — determinizm notu).</summary>
    private async Task<(List<EngineSegment> Segments, List<AutoScheduleUnplaceableDto> Unplaceable)> RunEngineAsync(
        IReadOnlyList<int> includedWorkOrderIds, DateTime fromUtc, int? scenarioId, CancellationToken ct)
    {
        var result = new List<EngineSegment>();
        var unplaceable = new List<AutoScheduleUnplaceableDto>();
        if (includedWorkOrderIds.Count == 0) return (result, unplaceable);

        // Priority DESC / PlannedEndDate ASC / Sequence sıralı — aynı WorkOrderId'nin satırları
        // bu sıralamada bitişik kalır (Priority+PlannedEndDate WO seviyesinde sabit), bu yüzden
        // ayrıca gruplamaya gerek yok; akış tek geçişte sırayla işlenir.
        var ops = await _repo.GetUnplannedOperationsAsync(includedWorkOrderIds, ct);
        if (ops.Count == 0) return (result, unplaceable);

        var machines = await _calendar.ListActiveMachinesAsync(ct);
        var machineNames = machines.ToDictionary(m => m.Id, m => m.Name ?? m.Code);

        var windowsRaw = await _calendar.ListWorkWindowsAsync(ct, scenarioId);
        var windowsByMachine = new Dictionary<int, Dictionary<byte, List<(short Start, short End)>>>();
        foreach (var w in windowsRaw)
        {
            if (!windowsByMachine.TryGetValue(w.MachineId, out var byDay))
                windowsByMachine[w.MachineId] = byDay = new Dictionary<byte, List<(short, short)>>();
            if (!byDay.TryGetValue(w.DayOfWeek, out var list))
                byDay[w.DayOfWeek] = list = new List<(short, short)>();
            list.Add((w.StartMinute, w.EndMinute));
        }
        foreach (var byDay in windowsByMachine.Values)
            foreach (var list in byDay.Values)
                list.Sort((a, b) => a.Start.CompareTo(b.Start));

        var holidaysRaw = await _calendar.ListHolidaysAsync(ct);
        var holidayDates = new HashSet<DateTime>();
        foreach (var h in holidaysRaw)
        {
            if (DateTime.TryParseExact(h.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d))
                holidayDates.Add(d.Date);
        }

        var occupiedByMachine = new Dictionary<int, List<(DateTime StartUtc, DateTime EndUtc)>>();
        foreach (var o in await _repo.GetOccupiedBlocksAsync(fromUtc, ct))
        {
            if (!occupiedByMachine.TryGetValue(o.MachineId, out var list))
                occupiedByMachine[o.MachineId] = list = new List<(DateTime, DateTime)>();
            list.Add((o.StartUtc, o.EndUtc));
        }

        // Aday makine önbelleği (aynı OperationId birden çok WO/operasyon satırında geçebilir).
        var candidateMachineCache = new Dictionary<int, IReadOnlyList<int>>();
        // Intra-WO precedence: bir WO içindeki bir sonraki operasyonun en erken başlangıcı,
        // bir önceki operasyonun bitişinden önce OLAMAZ.
        var prevEndByWorkOrder = new Dictionary<int, DateTime>();
        // Bir WO'nun bir operasyonu yerleştirilemezse ardılları da yerleştirilmemeli (precedence
        // zinciri kırılır; yoksa öncülü planlanmamış bir op planlanmış görünür — MEDIUM review bulgusu).
        var brokenWorkOrders = new HashSet<int>();

        foreach (var op in ops)
        {
            if (brokenWorkOrders.Contains(op.WorkOrderId))
            {
                unplaceable.Add(new AutoScheduleUnplaceableDto(op.WorkOrderOperationId, op.WorkOrderNo, op.OperationName,
                    "Aynı iş emrindeki öncül operasyon yerleştirilemedi (bağımlılık zinciri kırıldı)."));
                continue;
            }

            var earliestStart = prevEndByWorkOrder.TryGetValue(op.WorkOrderId, out var prevEnd) && prevEnd > fromUtc
                ? prevEnd : fromUtc;

            List<int> candidateMachineIds;
            if (op.MachineId is > 0)
            {
                // Elle atanmış makine yalnız AKTİF ise aday olur (aday-havuzu yolu zaten IsActive filtreler;
                // machineNames yalnız aktif makineleri içerir — MEDIUM review bulgusu).
                if (!machineNames.ContainsKey(op.MachineId.Value))
                {
                    unplaceable.Add(new AutoScheduleUnplaceableDto(op.WorkOrderOperationId, op.WorkOrderNo, op.OperationName,
                        "Operasyona atanmış makine pasif veya bulunamıyor."));
                    brokenWorkOrders.Add(op.WorkOrderId);
                    continue;
                }
                candidateMachineIds = new List<int> { op.MachineId.Value };
            }
            else
            {
                if (!candidateMachineCache.TryGetValue(op.OperationId, out var cached))
                {
                    cached = await _repo.GetCandidateMachineIdsAsync(op.OperationId, ct);
                    candidateMachineCache[op.OperationId] = cached;
                }
                candidateMachineIds = cached.ToList();
            }

            if (candidateMachineIds.Count == 0)
            {
                unplaceable.Add(new AutoScheduleUnplaceableDto(op.WorkOrderOperationId, op.WorkOrderNo, op.OperationName,
                    "Operasyon için makine tanımlı değil (WorkOrderOperation.MachineId boş ve OperationMachineTime'da aday makine yok)."));
                brokenWorkOrders.Add(op.WorkOrderId);
                continue;
            }

            (int MachineId, decimal SetupMinutes, List<(DateTime StartUtc, DateTime EndUtc)> Segments)? best = null;
            var anyValidDuration = false;

            foreach (var machineId in candidateMachineIds)
            {
                decimal prodMinutes;
                if (op.PlannedDuration is not null)
                {
                    prodMinutes = ((DurationUnit)op.DurationUnit) == DurationUnit.Hour
                        ? op.PlannedDuration.Value * 60m : op.PlannedDuration.Value;
                }
                else
                {
                    prodMinutes = await _durationResolver.ResolveDurationMinutesAsync(
                        op.OperationId, machineId, op.ItemId, op.RoutingId, op.PlannedQuantity ?? 1m, ct) ?? 0m;
                }
                var setupMinutes = await _durationResolver.ResolveSetupMinutesAsync(
                    op.OperationId, machineId, op.ItemId, op.RoutingId, ct) ?? 0m;
                var totalMinutes = prodMinutes + setupMinutes;
                if (totalMinutes <= 0m) continue; // süre çözümlenemedi/negatif — bu aday makineyi atla
                anyValidDuration = true;

                var occupied = occupiedByMachine.TryGetValue(machineId, out var occ)
                    ? occ : new List<(DateTime, DateTime)>();
                var windows = windowsByMachine.TryGetValue(machineId, out var wd)
                    ? wd : new Dictionary<byte, List<(short, short)>>();

                var segs = PlaceOnMachine(earliestStart, totalMinutes, occupied, windows, holidayDates);
                if (segs is null || segs.Count == 0) continue;

                if (best is null || segs[^1].EndUtc < best.Value.Segments[^1].EndUtc)
                    best = (machineId, setupMinutes, segs);
            }

            if (best is null)
            {
                // İki farklı başarısızlığı ayır (LOW review): süre hiç çözümlenemedi mi, yoksa süre
                // geçerli ama kapasite/pencere içinde planlama ufkuna sığmadı mı.
                var reason = anyValidDuration
                    ? "Kapasite/çalışma penceresi içinde yerleştirilemedi (planlama ufku aşıldı)."
                    : "Operasyon süresi tanımsız veya geçersiz (süre/setup 0 ya da negatif).";
                unplaceable.Add(new AutoScheduleUnplaceableDto(op.WorkOrderOperationId, op.WorkOrderNo, op.OperationName, reason));
                brokenWorkOrders.Add(op.WorkOrderId);
                continue;
            }

            var (chosenMachineId, setupMin, chosenSegs) = best.Value;

            if (!occupiedByMachine.TryGetValue(chosenMachineId, out var occList))
                occupiedByMachine[chosenMachineId] = occList = new List<(DateTime, DateTime)>();
            occList.AddRange(chosenSegs);

            // Setup/Production ayrımı: chosenSegs kronolojik sırada — ilk setupMin dakikası Setup,
            // kalanı Production. Sınırda kesişen segment ikiye bölünür.
            var setupSegs = new List<(DateTime, DateTime)>();
            var prodSegs = new List<(DateTime, DateTime)>();
            var remainingSetup = setupMin;
            foreach (var seg in chosenSegs)
            {
                var segMinutes = (decimal)(seg.EndUtc - seg.StartUtc).TotalMinutes;
                if (remainingSetup <= 0m)
                {
                    prodSegs.Add(seg);
                }
                else if (segMinutes <= remainingSetup)
                {
                    setupSegs.Add(seg);
                    remainingSetup -= segMinutes;
                }
                else
                {
                    var cut = seg.StartUtc.AddMinutes((double)remainingSetup);
                    setupSegs.Add((seg.StartUtc, cut));
                    prodSegs.Add((cut, seg.EndUtc));
                    remainingSetup = 0m;
                }
            }
            // totalMinutes>0 garantili (yukarıda filtrelendi) → setup+prod'dan en az biri dolu.

            var machineName = machineNames.TryGetValue(chosenMachineId, out var mn) ? mn : null;
            for (var i = 0; i < setupSegs.Count; i++)
            {
                result.Add(new EngineSegment(op.WorkOrderOperationId, op.WorkOrderNo, op.ItemName, op.OperationName,
                    chosenMachineId, machineName, (byte)MachineScheduleBlockType.Setup,
                    setupSegs[i].Item1, setupSegs[i].Item2, i + 1, setupSegs.Count));
            }
            for (var i = 0; i < prodSegs.Count; i++)
            {
                result.Add(new EngineSegment(op.WorkOrderOperationId, op.WorkOrderNo, op.ItemName, op.OperationName,
                    chosenMachineId, machineName, (byte)MachineScheduleBlockType.Production,
                    prodSegs[i].Item1, prodSegs[i].Item2, i + 1, prodSegs.Count));
            }

            var lastEnd = prodSegs.Count > 0 ? prodSegs[^1].Item2 : setupSegs[^1].Item2;
            prevEndByWorkOrder[op.WorkOrderId] = lastEnd;
        }

        return (result, unplaceable);
    }

    /// <summary>Bir makinede <paramref name="earliestStartUtc"/>'den itibaren
    /// <paramref name="totalMinutes"/> süresini yerleştirir; çalışma penceresi/tatil/mevcut blok
    /// nedeniyle kesintisiz olmayabilir (çok-parçalı sonuç). Segment listesi kronolojik sıradadır.
    /// Yerleştirme <see cref="MaxPlacementIterations"/> iterasyonunu (yaklaşık gün-bazlı adım —
    /// pratikte ~1-1.4 yıl ileri) aşarsa null döner (unplaceable).</summary>
    private static List<(DateTime StartUtc, DateTime EndUtc)>? PlaceOnMachine(
        DateTime earliestStartUtc,
        decimal totalMinutes,
        IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> occupiedUtc,
        IReadOnlyDictionary<byte, List<(short Start, short End)>> windowsByDay,
        HashSet<DateTime> holidayLocalDates)
    {
        if (totalMinutes <= 0m)
        {
            var pointLocal = UtcToLocal(earliestStartUtc);
            var pointUtc = LocalToUtc(pointLocal);
            return new List<(DateTime, DateTime)> { (pointUtc, pointUtc) };
        }

        var has247 = windowsByDay.Count == 0;
        var occupiedLocal = occupiedUtc
            .Select(o => (Start: UtcToLocal(o.StartUtc), End: UtcToLocal(o.EndUtc)))
            .OrderBy(o => o.Start)
            .ToList();

        var cursor = UtcToLocal(earliestStartUtc);
        var remaining = totalMinutes;
        var segments = new List<(DateTime Start, DateTime End)>();
        var iter = 0;

        while (remaining > 0m)
        {
            if (++iter > MaxPlacementIterations) return null;

            if (holidayLocalDates.Contains(cursor.Date))
            {
                cursor = cursor.Date.AddDays(1);
                continue;
            }

            DateTime availableEnd;
            if (has247)
            {
                availableEnd = cursor.Date.AddDays(1);
            }
            else
            {
                var dow = (byte)cursor.DayOfWeek; // Sunday=0..Saturday=6 — CalibraHub konvansiyonu
                var curMinuteOfDay = cursor.TimeOfDay.TotalMinutes;

                (short Start, short End)? win = null;
                if (windowsByDay.TryGetValue(dow, out var todays))
                {
                    foreach (var w in todays)
                    {
                        if (w.End > curMinuteOfDay) { win = w; break; }
                    }
                }
                if (win is null)
                {
                    cursor = cursor.Date.AddDays(1);
                    continue;
                }

                var winStart = cursor.Date.AddMinutes(win.Value.Start);
                var winEnd = cursor.Date.AddMinutes(win.Value.End);
                if (cursor < winStart) cursor = winStart;
                availableEnd = winEnd;
            }

            (DateTime Start, DateTime End)? blocking = null;
            foreach (var o in occupiedLocal)
            {
                if (o.Start < availableEnd && o.End > cursor)
                {
                    if (blocking is null || o.Start < blocking.Value.Start) blocking = o;
                }
            }
            if (blocking is not null)
            {
                var b = blocking.Value;
                if (b.Start <= cursor)
                {
                    cursor = b.End;
                    continue;
                }
                if (b.Start < availableEnd) availableEnd = b.Start;
            }

            var spanMinutes = (decimal)(availableEnd - cursor).TotalMinutes;
            if (spanMinutes <= 0m)
            {
                cursor = availableEnd;
                continue;
            }

            var take = Math.Min(spanMinutes, remaining);
            var segEnd = cursor.AddMinutes((double)take);
            segments.Add((cursor, segEnd));
            remaining -= take;
            cursor = segEnd;
        }

        return segments.Select(s => (StartUtc: LocalToUtc(s.Start), EndUtc: LocalToUtc(s.End))).ToList();
    }

    private static DateTime UtcToLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local);

    private static DateTime LocalToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // DST "spring forward" boşluğu — bir sonraki geçerli ana (+1 saat) kaydır (guard, nadir edge-case).
        if (TimeZoneInfo.Local.IsInvalidTime(unspecified))
            unspecified = unspecified.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
    }
}
