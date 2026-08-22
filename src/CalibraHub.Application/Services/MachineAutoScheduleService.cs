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
///
/// <para><b>Personel sonlu-kapasite kısıtı (Vardiya Senaryoları Faz 2, 2026-08-05):</b> makineler-arası
/// PAYLAŞILAN, sayı-tabanlı bir havuz — bir vardiya-günde aynı anda çalışabilecek üretim-operatörü
/// sayısı sınırlıdır (<c>IMachineCalendarRepository.GetShiftOperatorPoolAsync</c>), her operasyon
/// <c>OperationMachineTime.RequiredOperators</c> kadar tüketir. <b>fail-open ZORUNLU:</b> bir (ShiftId,
/// gün) için personel havuzu tanımsızsa (hiç aktif üretim-operatörü atanmamışsa) VEYA pencerenin
/// ShiftId'si yoksa (legacy fallback / has-247) kısıt hiç uygulanmaz — personel verisi girilmemiş
/// kurulumlarda motor Faz 1 davranışıyla BİREBİR aynı çalışır (regresyon yok). Bkz. <see cref="PlaceOnMachine"/>
/// XML doc'u (makine <c>occupied</c> blocking'in personel analoğu).</para>
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
        var (segments, unplaceable) = await RunEngineAsync(includedWorkOrderIds, fromUtc, scenarioId, reschedule: false, ct);
        return new AutoSchedulePreviewResultDto(BuildProposals(segments), unplaceable);
    }

    public async Task<AutoScheduleApplyResultDto> ApplyAsync(
        IReadOnlyList<int> includedWorkOrderIds, DateTime fromUtc, int? scenarioId, int? userId, CancellationToken ct)
    {
        var (segments, unplaceable) = await RunEngineAsync(includedWorkOrderIds, fromUtc, scenarioId, reschedule: false, ct);

        var operations = BuildOperationBlocks(segments);

        var created = operations.Count == 0 ? 0 : await _repo.InsertBlocksAsync(operations, userId, ct);
        return new AutoScheduleApplyResultDto(created, unplaceable.Count);
    }

    /// <summary>Blok Kilitle + Yeniden Çizelgele (2026-08-22) — bkz. <c>IMachineAutoScheduleService</c>
    /// XML doc'u. <c>includedWorkOrderIds</c> boş liste ile çağrılır (reschedule modunda motor
    /// <c>GetReschedulableOperationsAsync</c>'ten okur, WO filtresi yok).</summary>
    public async Task<ReschedulePreviewResultDto> ReschedulePreviewAsync(DateTime fromUtc, CancellationToken ct)
    {
        var (segments, unplaceable) = await RunEngineAsync(Array.Empty<int>(), fromUtc, scenarioId: null, reschedule: true, ct);
        var proposals = BuildProposals(segments);
        var releasedCount = await _repo.CountReleasableBlocksAsync(ct);
        return new ReschedulePreviewResultDto(proposals, unplaceable, releasedCount);
    }

    public async Task<RescheduleApplyResultDto> RescheduleApplyAsync(DateTime fromUtc, int? userId, CancellationToken ct)
    {
        var (segments, unplaceable) = await RunEngineAsync(Array.Empty<int>(), fromUtc, scenarioId: null, reschedule: true, ct);
        var operations = BuildOperationBlocks(segments);

        // Serbest set (kilitli/onaylı bloğu OLMAYAN açık-WO op'larına ait aktif Planlı bloklar)
        // her koşulda soft-delete edilir — bu turda yerleştirilemeyen (unplaceable) op'ların eski
        // bloğu da serbest kalır, tekrar planlanana kadar bloksuz kalır (kullanıcı kararı: "diğer
        // TÜM Planlı bloklar serbest bırakılır"). operations boş olsa dahi repo release adımını yapar.
        var (created, released) = await _repo.ApplyRescheduleAsync(operations, userId, ct);
        return new RescheduleApplyResultDto(created, released, unplaceable.Count);
    }

    private static List<AutoScheduleProposalDto> BuildProposals(List<EngineSegment> segments)
        => segments.Select(s => new AutoScheduleProposalDto(
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

    private static List<PlannedOperationBlocksDto> BuildOperationBlocks(List<EngineSegment> segments)
        => segments
            .GroupBy(s => s.WorkOrderOperationId)
            .Select(g => new PlannedOperationBlocksDto(
                g.Key,
                g.OrderBy(s => s.StartUtc)
                 .Select(s => new PlannedSegmentDto(s.MachineId, s.StartUtc, s.EndUtc, s.BlockType))
                 .ToList()))
            .ToList();

    private sealed record EngineSegment(
        int WorkOrderOperationId, string? WorkOrderNo, string? ItemName, string? OperationName,
        int MachineId, string? MachineName, byte BlockType, DateTime StartUtc, DateTime EndUtc,
        int SegmentIndex, int SegmentCount);

    /// <summary>Motor çekirdeği. Preview ve Apply tarafından AYNI şekilde çağrılır (bkz. sınıf
    /// XML doc'u — determinizm notu). <paramref name="reschedule"/>=false: Faz 3 "Otomatik Yerleştir"
    /// (placeable=<c>includedWorkOrderIds</c>'nin planlanmamış op'ları, occupied=TÜM aktif bloklar).
    /// <paramref name="reschedule"/>=true: Blok Kilitle + Yeniden Çizelgele (2026-08-22) —
    /// placeable=<c>GetReschedulableOperationsAsync</c> (TÜM açık WO, Kilitli/Onaylı bloğu olmayan
    /// op'lar; <paramref name="includedWorkOrderIds"/> bu modda YOK SAYILIR), occupied=yalnız
    /// Kilitli(2)/Onaylı(3) aktif bloklar.</summary>
    private async Task<(List<EngineSegment> Segments, List<AutoScheduleUnplaceableDto> Unplaceable)> RunEngineAsync(
        IReadOnlyList<int> includedWorkOrderIds, DateTime fromUtc, int? scenarioId, bool reschedule, CancellationToken ct)
    {
        var result = new List<EngineSegment>();
        var unplaceable = new List<AutoScheduleUnplaceableDto>();
        if (!reschedule && includedWorkOrderIds.Count == 0) return (result, unplaceable);

        // Priority DESC / PlannedEndDate ASC / Sequence sıralı — aynı WorkOrderId'nin satırları
        // bu sıralamada bitişik kalır (Priority+PlannedEndDate WO seviyesinde sabit), bu yüzden
        // ayrıca gruplamaya gerek yok; akış tek geçişte sırayla işlenir.
        var ops = reschedule
            ? await _repo.GetReschedulableOperationsAsync(ct)
            : await _repo.GetUnplannedOperationsAsync(includedWorkOrderIds, ct);
        if (ops.Count == 0) return (result, unplaceable);

        var machines = await _calendar.ListActiveMachinesAsync(ct);
        var machineNames = machines.ToDictionary(m => m.Id, m => m.Name ?? m.Code);

        // Personel kısıtı Faz 2 (2026-08-05): ListWorkWindowsAsync (kilitli, Gantt-tüketimi) yerine
        // motor-özel ShiftId'li türetme kullanılır — birebir aynı pencereler, sadece ShiftId taşır.
        var windowsRaw = await _calendar.ListScenarioMachineShiftWindowsAsync(scenarioId, ct);
        var windowsByMachine = new Dictionary<int, Dictionary<byte, List<(short Start, short End, int? ShiftId)>>>();
        foreach (var w in windowsRaw)
        {
            if (!windowsByMachine.TryGetValue(w.MachineId, out var byDay))
                windowsByMachine[w.MachineId] = byDay = new Dictionary<byte, List<(short, short, int?)>>();
            if (!byDay.TryGetValue(w.DayOfWeek, out var list))
                byDay[w.DayOfWeek] = list = new List<(short, short, int?)>();
            list.Add((w.StartMinute, w.EndMinute, w.ShiftId));
        }
        foreach (var byDay in windowsByMachine.Values)
            foreach (var list in byDay.Values)
                list.Sort((a, b) => a.Start.CompareTo(b.Start));

        // Personel havuzu (ShiftId,DayOfWeek)→aktif üretim-operatörü sayısı. Anahtar yoksa = "tanımsız"
        // → fail-open (kısıt uygulanmaz). Ledger makineler-arası PAYLAŞILAN, COMMIT edilen tüketim
        // defteri (LOCAL zaman — PlaceOnMachine'in tüm gün/pencere hesabıyla aynı zaman tabanında).
        var personnelPool = await _calendar.GetShiftOperatorPoolAsync(ct);
        var personnelLedger = new Dictionary<(int ShiftId, DateTime LocalDate), List<(DateTime Start, DateTime End, int Ops)>>();

        var holidaysRaw = await _calendar.ListHolidaysAsync(ct);
        var holidayDates = new HashSet<DateTime>();
        foreach (var h in holidaysRaw)
        {
            if (DateTime.TryParseExact(h.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d))
                holidayDates.Add(d.Date);
        }

        var occupiedByMachine = new Dictionary<int, List<(DateTime StartUtc, DateTime EndUtc)>>();
        foreach (var o in await _repo.GetOccupiedBlocksAsync(fromUtc, lockedConfirmedOnly: reschedule, ct))
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

            (int MachineId, decimal SetupMinutes, int RequiredOperators, List<(DateTime StartUtc, DateTime EndUtc, int? ShiftId)> Segments)? best = null;
            var anyValidDuration = false;
            var anyPersonnelBlocked = false;

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

                var requiredOperators = await _durationResolver.ResolveRequiredOperatorsAsync(
                    op.OperationId, machineId, op.ItemId, op.RoutingId, ct);

                var occupied = occupiedByMachine.TryGetValue(machineId, out var occ)
                    ? occ : new List<(DateTime, DateTime)>();
                var windows = windowsByMachine.TryGetValue(machineId, out var wd)
                    ? wd : new Dictionary<byte, List<(short, short, int?)>>();

                var segs = PlaceOnMachine(earliestStart, totalMinutes, occupied, windows, holidayDates,
                    requiredOperators, personnelPool, personnelLedger, out var personnelBlockedThisAttempt);
                if (personnelBlockedThisAttempt) anyPersonnelBlocked = true;
                if (segs is null || segs.Count == 0) continue;

                if (best is null || segs[^1].EndUtc < best.Value.Segments[^1].EndUtc)
                    best = (machineId, setupMinutes, requiredOperators, segs);
            }

            if (best is null)
            {
                // Üç farklı başarısızlığı ayır: süre hiç çözümlenemedi mi, personel kapasitesi
                // yetersiz mi (Faz 2), yoksa süre geçerli ama makine kapasitesi/penceresi içinde
                // planlama ufkuna sığmadı mı.
                var reason = !anyValidDuration
                    ? "Operasyon süresi tanımsız veya geçersiz (süre/setup 0 ya da negatif)."
                    : anyPersonnelBlocked
                        ? "Personel (vardiya operatör) kapasitesi yetersiz."
                        : "Kapasite/çalışma penceresi içinde yerleştirilemedi (planlama ufku aşıldı).";
                unplaceable.Add(new AutoScheduleUnplaceableDto(op.WorkOrderOperationId, op.WorkOrderNo, op.OperationName, reason));
                brokenWorkOrders.Add(op.WorkOrderId);
                continue;
            }

            var (chosenMachineId, setupMin, chosenRequiredOperators, chosenSegs) = best.Value;

            if (!occupiedByMachine.TryGetValue(chosenMachineId, out var occList))
                occupiedByMachine[chosenMachineId] = occList = new List<(DateTime, DateTime)>();
            occList.AddRange(chosenSegs.Select(s => (s.StartUtc, s.EndUtc)));

            // Personel defteri COMMIT (makineler-arası PAYLAŞILAN havuz) — yalnız ShiftId taşıyan
            // segmentler (fail-open: legacy/ShiftId-yok segmentler hiç deftere yazılmaz, çünkü
            // PlaceOnMachine onlarda zaten kısıt uygulamadı). LOCAL zamana çevrilir (ledger local).
            foreach (var seg in chosenSegs)
            {
                if (seg.ShiftId is not int segShiftId) continue;
                var localStart = UtcToLocal(seg.StartUtc);
                var localEnd = UtcToLocal(seg.EndUtc);
                var key = (segShiftId, localStart.Date);
                if (!personnelLedger.TryGetValue(key, out var ledgerList))
                    personnelLedger[key] = ledgerList = new List<(DateTime, DateTime, int)>();
                ledgerList.Add((localStart, localEnd, chosenRequiredOperators));
            }

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
                    prodSegs.Add((seg.StartUtc, seg.EndUtc));
                }
                else if (segMinutes <= remainingSetup)
                {
                    setupSegs.Add((seg.StartUtc, seg.EndUtc));
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
    /// nedeniyle kesintisiz olmayabilir (çok-parçalı sonuç). Segment listesi kronolojik sıradadır,
    /// her segment yerleştiği pencerenin <c>ShiftId</c>'sini taşır (legacy/has-247 → null).
    /// Yerleştirme <see cref="MaxPlacementIterations"/> iterasyonunu (yaklaşık gün-bazlı adım —
    /// pratikte ~1-1.4 yıl ileri) aşarsa null döner (unplaceable).
    ///
    /// <para><b>Personel kısıtı (Faz 2, 2026-08-05):</b> makine `occupied` blocking'inin (aşağıda,
    /// tek-aralık) BİREBİR ANALOĞU — ama tek bir engelleyici aralık yerine <paramref name="personnelLedger"/>
    /// üzerinde SÜPÜRME (sweep) ile "eşzamanlı tüketim + bu operasyonun ihtiyacı &gt; havuz" olan İLK
    /// aşım bloğu bulunur (<see cref="PersonnelCapacityBlock"/>). Pencerenin <c>ShiftId</c>'si null ise
    /// veya o (ShiftId,gün) için havuzda kayıt yoksa (personel verisi hiç girilmemiş) kısıt DEVREYE
    /// GİRMEZ — fail-open, mevcut çizelgeleme davranışı birebir korunur.</para></summary>
    private static List<(DateTime StartUtc, DateTime EndUtc, int? ShiftId)>? PlaceOnMachine(
        DateTime earliestStartUtc,
        decimal totalMinutes,
        IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> occupiedUtc,
        IReadOnlyDictionary<byte, List<(short Start, short End, int? ShiftId)>> windowsByDay,
        HashSet<DateTime> holidayLocalDates,
        int requiredOperators,
        IReadOnlyDictionary<(int ShiftId, byte DayOfWeek), int> personnelPool,
        IReadOnlyDictionary<(int ShiftId, DateTime LocalDate), List<(DateTime Start, DateTime End, int Ops)>> personnelLedger,
        out bool personnelBlocked)
    {
        personnelBlocked = false;

        if (totalMinutes <= 0m)
        {
            var pointLocal = UtcToLocal(earliestStartUtc);
            var pointUtc = LocalToUtc(pointLocal);
            return new List<(DateTime, DateTime, int?)> { (pointUtc, pointUtc, null) };
        }

        var has247 = windowsByDay.Count == 0;
        var occupiedLocal = occupiedUtc
            .Select(o => (Start: UtcToLocal(o.StartUtc), End: UtcToLocal(o.EndUtc)))
            .OrderBy(o => o.Start)
            .ToList();

        var cursor = UtcToLocal(earliestStartUtc);
        var remaining = totalMinutes;
        var segments = new List<(DateTime Start, DateTime End, int? ShiftId)>();
        var iter = 0;
        var emptyLedger = Array.Empty<(DateTime, DateTime, int)>();

        while (remaining > 0m)
        {
            if (++iter > MaxPlacementIterations) return null;

            if (holidayLocalDates.Contains(cursor.Date))
            {
                cursor = cursor.Date.AddDays(1);
                continue;
            }

            DateTime availableEnd;
            (short Start, short End, int? ShiftId)? win = null;
            if (has247)
            {
                availableEnd = cursor.Date.AddDays(1);
            }
            else
            {
                var dow = (byte)cursor.DayOfWeek; // Sunday=0..Saturday=6 — CalibraHub konvansiyonu
                var curMinuteOfDay = cursor.TimeOfDay.TotalMinutes;

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

            // Personel sonlu-kapasite kısıtı — makine `occupied` blocking'in analoğu (üstteki blok).
            // fail-open: ShiftId yok VEYA (ShiftId,gün) için havuz tanımsız → kısıt hiç uygulanmaz.
            if (win?.ShiftId is int windowShiftId
                && personnelPool.TryGetValue((windowShiftId, (byte)cursor.DayOfWeek), out var pool))
            {
                IReadOnlyList<(DateTime Start, DateTime End, int Ops)> ledgerEntries =
                    personnelLedger.TryGetValue((windowShiftId, cursor.Date), out var le) ? le : emptyLedger;
                var pBlock = PersonnelCapacityBlock(cursor, availableEnd, requiredOperators, pool, ledgerEntries);
                if (pBlock is not null)
                {
                    personnelBlocked = true;
                    var pb = pBlock.Value;
                    if (pb.Start <= cursor)
                    {
                        cursor = pb.End;
                        continue;
                    }
                    if (pb.Start < availableEnd) availableEnd = pb.Start;
                }
            }

            var spanMinutes = (decimal)(availableEnd - cursor).TotalMinutes;
            if (spanMinutes <= 0m)
            {
                cursor = availableEnd;
                continue;
            }

            var take = Math.Min(spanMinutes, remaining);
            var segEnd = cursor.AddMinutes((double)take);
            segments.Add((cursor, segEnd, has247 ? null : win!.Value.ShiftId));
            remaining -= take;
            cursor = segEnd;
        }

        return segments.Select(s => (StartUtc: LocalToUtc(s.Start), EndUtc: LocalToUtc(s.End), s.ShiftId)).ToList();
    }

    /// <summary>
    /// Personel kapasite süpürmesi (Faz 2, 2026-08-05) — <paramref name="ledger"/>'daki aralıkların
    /// (her biri kendi <c>Ops</c> tüketimiyle) [<paramref name="cursor"/>, <paramref name="availableEnd"/>)
    /// içindeki eşzamanlılık profilini çıkarır ve "eşzamanlı tüketim + <paramref name="requiredOperators"/>
    /// &gt; <paramref name="pool"/>" olan İLK (en erken başlayan) aşım bloğunu döner. Aşım yoksa null —
    /// çağıran bunu makine `occupied` blocking'iyle AYNI şekilde yorumlar (blok cursor'ı kapsıyorsa
    /// cursor'ı bloğun sonuna ilerlet, kapsamıyorsa availableEnd'i bloğun başına kısalt).
    /// </summary>
    private static (DateTime Start, DateTime End)? PersonnelCapacityBlock(
        DateTime cursor, DateTime availableEnd, int requiredOperators, int pool,
        IReadOnlyList<(DateTime Start, DateTime End, int Ops)> ledger)
    {
        if (requiredOperators <= 0) return null;
        // Bu operasyon tek başına havuzu aşıyorsa ledger'dan bağımsız olarak [cursor, availableEnd)
        // tamamı aşım bloğudur (ledger boş olsa dahi — Count==0 fast-path'in altına alınmadı).
        if (requiredOperators > pool) return (cursor, availableEnd);
        if (ledger.Count == 0) return null;

        var events = new SortedDictionary<DateTime, int>();
        void AddEvent(DateTime t, int delta)
        {
            if (t < cursor) t = cursor;
            if (t > availableEnd) t = availableEnd;
            events.TryGetValue(t, out var cur);
            events[t] = cur + delta;
        }
        foreach (var e in ledger)
        {
            if (e.End <= cursor || e.Start >= availableEnd) continue; // aralık dışı
            AddEvent(e.Start, e.Ops);
            AddEvent(e.End, -e.Ops);
        }
        if (events.Count == 0) return null;

        var times = events.Keys.ToList();
        var concurrent = 0;
        DateTime? blockStart = null;
        for (var idx = 0; idx < times.Count; idx++)
        {
            concurrent += events[times[idx]];
            var segStart = times[idx];
            var segEnd = idx + 1 < times.Count ? times[idx + 1] : availableEnd;
            if (segEnd <= segStart) continue;

            var overflow = concurrent + requiredOperators > pool;
            if (overflow && blockStart is null) blockStart = segStart;
            else if (!overflow && blockStart is not null) return (blockStart.Value, segStart);
        }
        return blockStart is not null ? (blockStart.Value, availableEnd) : null;
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
