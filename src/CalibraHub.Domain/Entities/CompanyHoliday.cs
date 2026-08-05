namespace CalibraHub.Domain.Entities;

/// <summary>
/// Makine Planlama Faz 2 (2026-08-05) — resmi tatil / şirket kapalı günü. Gantt'ta müsaitlik
/// dışı zaman ile birlikte gölgelenir (frontend). Per-company DB (CompanyId kolonu yok).
/// </summary>
public sealed class CompanyHoliday
{
    public int Id { get; set; }

    /// <summary>Yerel DATE (saat bileşeni yok).</summary>
    public DateTime HolidayDate { get; set; }

    public string? Name { get; set; }

    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; set; }
    public DateTime Created { get; set; }
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
