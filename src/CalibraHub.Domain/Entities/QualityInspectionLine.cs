using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Muayene ölçüm satırı (native). Plandan snapshot'lanan tolerans + operatörün girdiği ölçülen
/// değer. Sayısal satırda Result = (Measured tolerans içinde mi?) otomatik hesaplanır.
/// </summary>
[Description("Muayene ölçüm satırı. Sayısal satırda Result tolerans karşı otomatik; uygunsuzda hata kodu zorunlu.")]
public sealed class QualityInspectionLine
{
    public int Id { get; init; }

    [Description("Bağlı muayene kaydı. FK -> QualityInspection.Id.")]
    public int InspectionId { get; init; }

    [Description("Kaynak plan satırı (izlenebilirlik). FK -> QualityInspectionPlanLine.Id (nullable).")]
    public int? PlanLineId { get; set; }

    public required string CharacteristicName { get; set; }

    // Plandan snapshot (kayıt anında dondurulur)
    public decimal? Nominal { get; set; }
    public decimal? LowerTol { get; set; }
    public decimal? UpperTol { get; set; }

    [Description("Ölçülen değer (operatör girer).")]
    public decimal? Measured { get; set; }

    [Description("true = sayısal (tolerans karşı otomatik değerlendirilir); false = görsel (Result operatörden gelir).")]
    public bool IsNumeric { get; set; } = true;

    public InspectionLineResult Result { get; set; } = InspectionLineResult.NotEvaluated;

    [Description("Uygunsuz satırda seçilen hata kodu. FK -> QualityDefectCode.Id.")]
    public int? DefectCodeId { get; set; }

    public int OrderNo { get; set; }
    public string? Notes { get; set; }

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
