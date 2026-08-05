namespace CalibraHub.Application.Contracts;

/// <summary>
/// Makine Planlama Faz 2 (2026-08-05) — haftalık makine müsaitlik takvimi + resmi tatil. API
/// sözleşmesi KİLİTLİ — frontend (MachineCalendar admin ekranı + Gantt gölgeleme) bu DTO'lara
/// göre yazıldı, alan adı/tipini değiştirme.
/// Konvansiyon: DayOfWeek 0=Pazar..6=Cumartesi (JS Date.getDay() ile birebir); StartMinute/
/// EndMinute yerel gün-ortası dakikası 0..1440 (duvar saati — UTC DEĞİL, haftalık tekrar);
/// tatil Date = "yyyy-MM-dd" (yerel DATE, saat bileşeni yok).
/// </summary>
public sealed record MachineWorkWindowDto(
    int Id,
    int MachineId,
    byte DayOfWeek,
    short StartMinute,
    short EndMinute);

public sealed record SaveMachineWorkWindowRequest(
    int Id,
    int MachineId,
    byte DayOfWeek,
    short StartMinute,
    short EndMinute);

public sealed record HolidayDto(
    int Id,
    string Date,
    string? Name);

public sealed record SaveHolidayRequest(
    int Id,
    string Date,
    string? Name);
