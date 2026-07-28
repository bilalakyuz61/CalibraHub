using System.ComponentModel;
using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Kalite hata/uygunsuzluk kodu kataloğu. Uygunsuz muayene satırında seçilir.
/// Kod kullanıcıdan alınmaz — isimden türetilir (K6 kuralı); isim üzerinden uniqueness.
/// ActivityReason deseninin muadili.
/// </summary>
[Description("Kalite hata kodu kataloğu (kategori bazlı). Uygunsuz ölçüm satırında seçilir.")]
public sealed class QualityDefectCode
{
    public int Id { get; init; }

    [Description("Hata kategorisi (Boyutsal/Görsel/Fonksiyonel/Malzeme/Diğer).")]
    public DefectCategory Category { get; init; }

    [Description("Otomatik türetilen kod (isimden). UI'da gösterilmez/alınmaz.")]
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>Opsiyonel rozet rengi (#RRGGBB ya da #RRGGBBAA).</summary>
    public string? ColorHex { get; init; }

    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }

    public void EnsureValid()
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(Code), "Hata kodu zorunludur.");
        DomainException.ThrowIf(Code.Length > 50, "Hata kodu en fazla 50 karakter olabilir.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(Name), "Hata adı zorunludur.");
        DomainException.ThrowIf(Name.Length > 200, "Hata adı en fazla 200 karakter olabilir.");
        if (!string.IsNullOrWhiteSpace(ColorHex))
        {
            var c = ColorHex.Trim();
            DomainException.ThrowIf(!c.StartsWith('#') || (c.Length != 7 && c.Length != 9),
                "Renk kodu '#RRGGBB' veya '#RRGGBBAA' formatında olmalıdır.");
        }
    }
}
