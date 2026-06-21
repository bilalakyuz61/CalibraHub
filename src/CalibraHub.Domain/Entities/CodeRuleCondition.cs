using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir <see cref="CodeRule"/>'un uygulanma şartı. Birden fazla şart AND ile birleşir.
/// Şart hiç yoksa kural "default/fallback" — diğer kurallar eşleşmezse devreye girer.
///
/// FieldType:
///   'Field'  → Contact/Item DB kolonu (GroupId, City, TaxCity, Category vb.)
///   'Widget' → WidgetTra değeri (form widget — MUSTERI_TIPI, BOLGE vb.)
/// </summary>
[Description("Kural uygulanma şartı — Contact.GroupId=5 gibi.")]
public sealed class CodeRuleCondition
{
    public int Id { get; init; }
    public int RuleId { get; set; }

    /// <summary>'Field' (DB kolonu) veya 'Widget' (WidgetTra).</summary>
    public required string FieldType { get; set; }

    /// <summary>Alan adı — 'GroupId', 'City', 'MUSTERI_TIPI' vb.</summary>
    public required string FieldName { get; set; }

    /// <summary>Operatör: '=', '!=', 'in', 'notin', 'startsWith', 'isNull', 'isNotNull'.</summary>
    public required string Operator { get; set; }

    /// <summary>Tek değer veya JSON array (in/notin için: '["5","10"]'). isNull/isNotNull için NULL.</summary>
    public string? Value { get; set; }
}
