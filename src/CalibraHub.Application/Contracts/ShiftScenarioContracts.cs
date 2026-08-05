namespace CalibraHub.Application.Contracts;

/// <summary>
/// Vardiya Senaryoları (2026-08-05) — Makine Çalışma Takvimi'nin senaryo-tabanlı yükseltmesi.
/// <see cref="MachineWorkWindowDto"/>/<see cref="SaveMachineWorkWindowRequest"/> KİLİTLİ
/// sözleşmesine dokunulmaz; bu DTO'lar yeni senaryo/atama CRUD'u için ayrı bir sözleşmedir.
/// Bkz. <c>SqlMachineCalendarRepository.ListWorkWindowsAsync</c> (senaryo → pencere türetme).
/// </summary>
public sealed record ShiftScenarioDto(
    int Id,
    string Name,
    string? Description,
    bool IsDefault,
    bool IsActive);

public sealed record SaveShiftScenarioRequest(
    int Id,
    string Name,
    string? Description,
    bool IsDefault);

/// <summary>Senaryo × Makine × Vardiya × Gün ataması. <c>DaysMask</c>: bit0=Pazar..bit6=Cumartesi.
/// <c>MachineName</c>/<c>ShiftName</c> yalnızca gösterim kolaylığı için — karar/karşılaştırma
/// mantığında kullanılmaz (ID tabanlı eşleştirme kuralı).</summary>
public sealed record ScenarioMachineShiftDto(
    int Id,
    int ScenarioId,
    int MachineId,
    int ShiftId,
    byte DaysMask,
    string? MachineName = null,
    string? ShiftName = null);

public sealed record SaveScenarioMachineShiftRequest(
    int Id,
    int ScenarioId,
    int MachineId,
    int ShiftId,
    byte DaysMask);

/// <summary>
/// Vardiya Senaryoları Faz 2 (2026-08-05) — motor-özel (internal) pencere türetmesi.
/// <see cref="MachineWorkWindowDto"/> KİLİTLİ sözleşmesinin AYNI türetme mantığını (bkz.
/// <c>SqlMachineCalendarRepository.ListWorkWindowsAsync</c>) kullanır ama ayrıca <c>ShiftId</c>
/// taşır — yalnız <c>MachineAutoScheduleService</c> personel sonlu-kapasite kısıtı için tüketir,
/// Gantt/gölgeleme bunu KULLANMAZ (o <see cref="MachineWorkWindowDto"/>'da kalır). Legacy fallback
/// (senaryoda hiç atama yok) veya has-247 durumunda <c>ShiftId=null</c> döner — fail-open: personel
/// kısıtı bu pencerelerde UYGULANMAZ.
/// </summary>
public sealed record MachineShiftWindowDto(
    int MachineId,
    byte DayOfWeek,
    short StartMinute,
    short EndMinute,
    int? ShiftId);
