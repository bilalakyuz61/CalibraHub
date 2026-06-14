namespace CalibraHub.Application.Contracts;

/// <summary>
/// Vardiya tanım DTO'su (2026-05-21 — Faz 3). UI saatleri "HH:mm" string olarak alır;
/// TimeSpan'a service tarafında parse edilir.
/// </summary>
public sealed record ShiftDto(
    int Id,
    string Code,
    string Name,
    string StartTime,          // "HH:mm" (UI kolay tüketsin)
    string EndTime,
    bool IsOvernight,
    int DurationMinutes,
    string? ColorHex,
    int SortOrder,
    bool IsActive,
    DateTime Created,
    DateTime? Updated,
    // 2026-05-21: Vardiya içi molalar (çay/yemek) — net çalışma süresi hesabı için.
    IReadOnlyList<ShiftBreakDto>? Breaks = null,
    int TotalBreakMinutes = 0,
    int NetWorkMinutes = 0);

public sealed record ShiftBreakDto(
    int Id,
    int ShiftId,
    string Name,
    string StartTime,          // "HH:mm"
    string EndTime,
    int DurationMinutes,
    int SortOrder);

public sealed record SaveShiftRequest(
    int? Id,
    string Code,
    string Name,
    string StartTime,          // "HH:mm" — service TimeSpan.Parse eder
    string EndTime,
    string? ColorHex,
    int SortOrder = 0,
    bool IsActive = true,
    // 2026-05-21: Save sırasında tüm aralar replace edilir (delete + insert pattern).
    // null = aralara dokunma; [] = tüm araları temizle; dolu liste = bu liste ile değiştir.
    IReadOnlyList<SaveShiftBreakRequest>? Breaks = null);

public sealed record SaveShiftBreakRequest(
    string Name,
    string StartTime,          // "HH:mm"
    string EndTime,
    int SortOrder = 0);

/// <summary>
/// Personel-Vardiya-Gün eşleştirme DTO'su (haftalık tekrar pattern).
/// </summary>
public sealed record ShiftAssignmentDto(
    int Id,
    int PersonnelId,
    string? PersonnelName,
    int ShiftId,
    string? ShiftCode,
    string? ShiftName,
    DayOfWeek DayOfWeek,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive);

public sealed record SaveShiftAssignmentRequest(
    int? Id,
    int PersonnelId,
    int ShiftId,
    DayOfWeek DayOfWeek,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive = true);

/// <summary>
/// Bir personelin haftalık vardiya matrisi (UI grid). 7 günün her biri için
/// atama varsa ShiftId+ShiftName, yoksa null.
/// </summary>
public sealed record PersonnelWeeklyShiftDto(
    int PersonnelId,
    string? PersonnelName,
    IReadOnlyDictionary<DayOfWeek, ShiftAssignmentDto?> Days);
