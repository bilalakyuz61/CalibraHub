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

    /// <summary>Aktif makinelerin aktif haftalık müsaitlik pencereleri.</summary>
    Task<IReadOnlyList<MachineWorkWindowDto>> ListWorkWindowsAsync(CancellationToken ct);

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
}
