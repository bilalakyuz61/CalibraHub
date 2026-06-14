using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// 2026-06-12 — Bir <see cref="DataVisibilityRule"/>'ın kısıtladığı tek değer (kural birden çok
/// değer taşıyabilir → IN/set). ID-bazlı eşleştirme kuralı gereği kolon/FK alanlarında
/// <see cref="ValueId"/> (örn. CariGroup.Id) tercih edilir; widget/string alanlarda
/// <see cref="ValueText"/> (WidgetTra.Value) kullanılır.
/// </summary>
[Description("Veri görünürlük kuralının kısıtladığı değer (ValueId = ID-bazlı hedef, ValueText = widget/string değeri). 2026-06-12.")]
public sealed class DataVisibilityRuleValue
{
    public int Id { get; init; }
    public int RuleId { get; set; }

    /// <summary>ID-bazlı hedef değer (örn. CariGroup.Id). Kolon/FK kurallarında bu doldurulur.</summary>
    public int? ValueId { get; set; }

    /// <summary>String hedef değer (WidgetTra.Value). Widget/string kurallarında bu doldurulur.</summary>
    public string? ValueText { get; set; }
}
