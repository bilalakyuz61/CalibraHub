using System.ComponentModel;
using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// 2026-06-12 — Satır bazlı veri görünürlük kuralı (alan-değer bazlı, kısıtlama modeli).
///
/// Bir entity alanının (kolon veya widget) belirli değer(ler)ine sahip satırlar yalnızca
/// <see cref="Grants"/> içindeki kullanıcı/departmanlara görünür; kurala değmeyen satır
/// HERKESE AÇIKtır (default-allow). Sahiplik kavramı YOKtur — görünürlük tamamen satırın
/// alan değerinden türer, kayıt eklerken hiçbir şey damgalanmaz.
///
/// **DB tablo:** DataVisibilityRule (per-company DB — Contact/CariGroup/WidgetMas ile aynı bağlantı).
/// </summary>
[Description("Satır görünürlük kuralı: bir entity alanının değer(ler)ini belirli kullanıcı/departmanlara kısıtlar. 2026-06-12.")]
public sealed class DataVisibilityRule
{
    public int Id { get; init; }

    /// <summary>Entity anchor — PermissionDef.FormCode uzayı (örn. 'CONTACTS'). Kuralın hangi veri nesnesine ait olduğunu belirler.</summary>
    public required string FormCode { get; set; }

    /// <summary>Hedef alan türü: kolon mu, widget mı.</summary>
    public DataVisibilityFieldKind FieldKind { get; set; } = DataVisibilityFieldKind.Column;

    /// <summary>Kolon adı (örn. 'ContactGroupId') veya WidgetCode.</summary>
    public required string FieldKey { get; set; }

    /// <summary>
    /// 2026-06-13 — Karşılaştırma operatörü. Kurala TAKILAN (operatör + değer(ler)i sağlayan)
    /// satırlar HERKESE gizlenir. Geçerli değerler:
    /// eq, neq, gt, gte, lt, lte, between, in, not_in, like, not_like, startswith, endswith,
    /// isnull, isnotnull. Varsayılan 'eq'.
    /// </summary>
    public string Operator { get; set; } = "eq";

    /// <summary><see cref="FieldKind"/> = Widget ise WidgetMas.Id (EAV ön-sorgusu için). Aksi halde NULL.</summary>
    public int? WidgetId { get; set; }

    /// <summary>Admin etiketi (örn. 'ABC grubu gizliliği').</summary>
    public required string Name { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime Created { get; init; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
    public int? CreatedById { get; set; }
    public int? UpdatedById { get; set; }

    /// <summary>Kısıtlanan değer(ler) — IN/set. Repository yükleme sırasında doldurur.</summary>
    public List<DataVisibilityRuleValue> Values { get; init; } = new();

    /// <summary>İzinli principal'lar (kullanıcı VEYA departman). Repository yükleme sırasında doldurur.</summary>
    public List<DataVisibilityGrant> Grants { get; init; } = new();

    public void EnsureValid()
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(FormCode), "FormCode zorunlu.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(FieldKey), "FieldKey zorunlu.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(Name), "Kural adı zorunlu.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(Operator), "Operatör zorunlu.");
        DomainException.ThrowIf(FieldKind == DataVisibilityFieldKind.Widget && (WidgetId is null || WidgetId <= 0),
            "Widget alanı kuralında WidgetId zorunlu.");
    }
}
