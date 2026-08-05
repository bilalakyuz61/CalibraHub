using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Makine Planlama Faz 2 (2026-08-05) — haftalık makine müsaitlik takvimi (<c>MachineWorkWindow</c>)
/// + resmi tatil (<c>CompanyHoliday</c>). Bkz. entity XML doc'ları + <c>MachineCalendarContracts.cs</c>.
/// Tek sorumluluk: <c>IMachineScheduleRepository</c>'den ayrı tutuldu (Gantt canvas veri katmanı
/// vs. takvim/tatil tanım katmanı); <c>SqlMachineScheduleRepository</c> Gantt gölgeleme verisini
/// bu repodan okur.
/// </summary>
public interface IMachineCalendarRepository
{
    /// <summary>Aktif makineler (Machine.IsActive=1, per-company). Admin ekranı için.</summary>
    Task<IReadOnlyList<ScheduleMachineDto>> ListActiveMachinesAsync(CancellationToken ct);

    /// <summary>
    /// Aktif makinelerin aktif haftalık müsaitlik pencereleri. Vardiya Senaryoları (2026-08-05)
    /// yükseltmesi: <paramref name="scenarioId"/> null ise <c>IsDefault=1</c> senaryo çözülür;
    /// çözülen senaryonun <c>ScenarioMachineShift</c> atamaları varsa pencereler bunlardan TÜRETİLİR
    /// (Shift.StartTime/EndTime/IsOvernight × DaysMask). Senaryoda hiç atama yoksa (veya hiç senaryo
    /// yoksa) eski <c>MachineWorkWindow</c> okumasına LEGACY FALLBACK yapılır (kademeli geçiş, sıfır
    /// veri kaybı). Dönen liste her durumda <see cref="MachineWorkWindowDto"/> sözleşmesindedir —
    /// türetilmiş satırlarda <c>Id=0</c> (sentetik, kullanılmaz).
    /// </summary>
    Task<IReadOnlyList<MachineWorkWindowDto>> ListWorkWindowsAsync(CancellationToken ct, int? scenarioId = null);

    /// <summary>Tekil pencere (audit eski-değer dökümü için). Bulunamazsa/pasifse null.</summary>
    Task<MachineWorkWindowDto?> GetWorkWindowAsync(int id, CancellationToken ct);

    /// <summary>Oluştur/güncelle (Id&lt;=0 → yeni). MachineId'nin bu şirkete ait aktif bir makine
    /// olduğu doğrulanır; değilse <see cref="ArgumentException"/>.</summary>
    Task<int> SaveWorkWindowAsync(SaveMachineWorkWindowRequest request, int? userId, CancellationToken ct);

    /// <summary>Soft-delete (IsActive=0).</summary>
    Task DeleteWorkWindowAsync(int id, int? userId, CancellationToken ct);

    /// <summary>Aktif resmi tatiller (tüm tarihler — frontend görünür aralığa göre filtreler).</summary>
    Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(CancellationToken ct);

    /// <summary>Tekil tatil (audit eski-değer dökümü için). Bulunamazsa/pasifse null.</summary>
    Task<HolidayDto?> GetHolidayAsync(int id, CancellationToken ct);

    /// <summary>Oluştur/güncelle (Id&lt;=0 → yeni). Aynı tarihte aktif başka bir tatil varsa
    /// <see cref="ArgumentException"/> (UX_CompanyHoliday_Date filtered unique index ile uyumlu).</summary>
    Task<int> SaveHolidayAsync(SaveHolidayRequest request, int? userId, CancellationToken ct);

    /// <summary>Soft-delete (IsActive=0).</summary>
    Task DeleteHolidayAsync(int id, int? userId, CancellationToken ct);

    // ── Vardiya Senaryoları (2026-08-05) ──────────────────────────────────────────

    /// <summary>Aktif senaryolar (IsDefault önce, sonra ada göre).</summary>
    Task<IReadOnlyList<ShiftScenarioDto>> ListScenariosAsync(CancellationToken ct);

    /// <summary>Tekil senaryo. Bulunamazsa/pasifse null.</summary>
    Task<ShiftScenarioDto?> GetScenarioAsync(int id, CancellationToken ct);

    /// <summary>Oluştur/güncelle (Id&lt;=0 → yeni). IsDefault=true verilirse önce diğer tüm
    /// senaryoların IsDefault'u 0'a çekilir (tek varsayılan — UX_ShiftScenario_Default ile uyumlu).</summary>
    Task<int> SaveScenarioAsync(SaveShiftScenarioRequest request, int? userId, CancellationToken ct);

    /// <summary>Soft-delete (IsActive=0). Varsayılan senaryo silinemez —
    /// <see cref="ArgumentException"/> fırlatır (önce başka bir senaryo varsayılan yapılmalı).</summary>
    Task DeleteScenarioAsync(int id, int? userId, CancellationToken ct);

    /// <summary>Bir senaryonun aktif makine×vardiya×gün atamaları (Machine/Shift adları join'li).</summary>
    Task<IReadOnlyList<ScenarioMachineShiftDto>> ListScenarioMachineShiftsAsync(int scenarioId, CancellationToken ct);

    /// <summary>Oluştur/güncelle (Id&lt;=0 → yeni). MachineId'nin bu şirkete ait aktif bir makine,
    /// ScenarioId/ShiftId'nin aktif kayıtlar olduğu doğrulanır; değilse <see cref="ArgumentException"/>.</summary>
    Task<int> SaveScenarioMachineShiftAsync(SaveScenarioMachineShiftRequest request, int? userId, CancellationToken ct);

    /// <summary>Soft-delete (IsActive=0).</summary>
    Task DeleteScenarioMachineShiftAsync(int id, int? userId, CancellationToken ct);
}
