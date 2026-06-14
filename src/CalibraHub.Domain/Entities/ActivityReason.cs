using System.ComponentModel;
using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("Saha aktivite alt sebebi (2026-05-21 — Faz 2). Admin tanımlar: ör. Arıza tipi için 'Sensör', 'Elektrik', 'Mekanik'; Malzeme Bekleme için 'Tedarikçi gecikti', 'Stok bitti'. Operatör Durum Değiştir → sebep dropdown'unda görür. ActivityType + Code aktif kayıtlar için unique.")]
public sealed class ActivityReason
{
    public int Id { get; init; }
    public WorkOrderActivityType ActivityType { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    /// <summary>Opsiyonel rozet rengi (#RRGGBB ya da #RRGGBBAA). UI sebep chip'inin tonunu belirler.</summary>
    public string? ColorHex { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;

    // Audit dörtlüsü
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }

    public void EnsureValid()
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(Code),
            "Sebep kodu zorunludur.");
        DomainException.ThrowIf(Code.Length > 50,
            "Sebep kodu en fazla 50 karakter olabilir.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(Name),
            "Sebep adı zorunludur.");
        DomainException.ThrowIf(Name.Length > 200,
            "Sebep adı en fazla 200 karakter olabilir.");
        if (!string.IsNullOrWhiteSpace(ColorHex))
        {
            var c = ColorHex.Trim();
            DomainException.ThrowIf(!c.StartsWith('#') || (c.Length != 7 && c.Length != 9),
                "Renk kodu '#RRGGBB' veya '#RRGGBBAA' formatında olmalıdır.");
        }
    }
}
