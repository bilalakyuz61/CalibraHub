using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("Üretim operasyon sözlüğü — Kesim, Bükme, Boya, Kaynak, Montaj gibi standart üretim adımları. Routing (Faz 3) bir item için bu operasyonların sıralı bir dizisini tanımlar; WorkOrderOperation çalışma zamanında atama tutar.")]
public sealed class Operation
{
    public int Id { get; init; }
    public int CompanyId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Tahmini standart süre (DurationUnit ile yorumlanır). Routing/planlama için baz.</summary>
    public decimal? StandardDuration { get; init; }
    public DurationUnit DurationUnit { get; init; } = DurationUnit.Minute;

    /// <summary>Saatlik işçilik maliyeti (opsiyonel; raporlama/maliyet hesabı için).</summary>
    public decimal? HourlyRate { get; init; }

    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;

    public DateTime Created { get; init; }
    public DateTime? Updated { get; init; }
}
