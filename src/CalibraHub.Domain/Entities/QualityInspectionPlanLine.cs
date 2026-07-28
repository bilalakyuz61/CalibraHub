using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Muayene planı karakteristik satırı. Muayene kaydı açılınca bu satırlar ölçüm satırlarına
/// snapshot'lanır (plan sonradan değişse kayıt bozulmaz).
/// </summary>
[Description("Muayene planı karakteristiği (nominal + alt/üst tolerans + yöntem/alet).")]
public sealed class QualityInspectionPlanLine
{
    public int Id { get; init; }

    [Description("Bağlı plan. FK -> QualityInspectionPlan.Id.")]
    public int PlanId { get; init; }

    public required string CharacteristicName { get; set; }

    [Description("Hedef değer (sayısal karakteristikte).")]
    public decimal? Nominal { get; set; }

    [Description("Alt tolerans sınırı (null = alt sınır serbest).")]
    public decimal? LowerTol { get; set; }

    [Description("Üst tolerans sınırı (null = üst sınır serbest).")]
    public decimal? UpperTol { get; set; }

    [Description("Ölçü birimi. FK -> Unit.Id (nullable).")]
    public int? UnitId { get; set; }

    public string? Method { get; set; }
    public string? GaugeName { get; set; }

    [Description("true = sayısal ölçüm (tolerans karşı otomatik değerlendirilir); false = görsel uygun/uygunsuz.")]
    public bool IsNumeric { get; set; } = true;

    public int OrderNo { get; set; }

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
