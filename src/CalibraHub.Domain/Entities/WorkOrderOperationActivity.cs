using System.ComponentModel;
using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("Üretim sahası aktivite log satırı (2026-05-20 — Faz 1 MVP). Bir WorkOrderOperation'da yapılan her eylem (Hazırlık/Üretim/Malzeme Bekleme/Arıza/Mola/Vardiya/Kalite/Planlı Durma/Diğer) ayrı satır olarak kaydedilir. Filtered unique index ile aynı operasyonda anda yalnız bir 'aktif' (EndedAt NULL) satır olabilir. Production aktivitesi miktar/fire tutar, diğerleri yalnız süre.")]
public sealed class WorkOrderOperationActivity
{
    public int Id { get; init; }
    public int WorkOrderOperationId { get; init; }
    public int PersonnelId { get; init; }
    public WorkOrderActivityType ActivityType { get; init; }

    /// <summary>Opsiyonel sebep FK (Faz 2 — ActivityReason tablosu). NULL = serbest.</summary>
    public int? ActivityReasonId { get; init; }

    public DateTime StartedAt { get; init; }
    /// <summary>NULL = aktivite hâlâ devam ediyor (an aktif). EndActivity ile set edilir.</summary>
    public DateTime? EndedAt { get; init; }

    /// <summary>Yalnız Production tipinde dolar (üretilen iyi miktar).</summary>
    public decimal? Quantity { get; init; }
    /// <summary>Yalnız Production tipinde dolar (fire/iskarta miktar).</summary>
    public decimal? ScrapQuantity { get; init; }

    public string? Notes { get; init; }

    // Audit — standart kolon seti
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }

    /// <summary>
    /// Domain invariantları — kaydetmeden önce service çağırır.
    /// - Other tipinde Notes zorunlu (sebep açıklaması).
    /// - Production'da Quantity 0 veya pozitif (negatif olmaz; iade ayrı bir aksiyon).
    /// - EndedAt verilmişse StartedAt'tan büyük olmalı.
    /// </summary>
    public void EnsureValid()
    {
        DomainException.ThrowIf(WorkOrderOperationId <= 0,
            "Aktivite bir operasyona bağlı olmalı.");
        DomainException.ThrowIf(PersonnelId <= 0,
            "Aktiviteyi yapan personel seçilmelidir.");
        DomainException.ThrowIf(StartedAt == default,
            "Aktivite başlangıç zamanı boş olamaz.");
        DomainException.ThrowIf(EndedAt.HasValue && EndedAt.Value < StartedAt,
            "Aktivite bitiş zamanı, başlangıç zamanından önce olamaz.");

        if (ActivityType == WorkOrderActivityType.Other)
        {
            DomainException.ThrowIf(string.IsNullOrWhiteSpace(Notes),
                "'Diğer' aktivite tipinde açıklama (Notes) zorunludur.");
        }

        if (Quantity.HasValue)
        {
            DomainException.ThrowIf(Quantity.Value < 0,
                "Üretim miktarı negatif olamaz.");
            DomainException.ThrowIf(ActivityType != WorkOrderActivityType.Production,
                "Miktar yalnız 'Üretim' tipinde girilebilir.");
        }
        if (ScrapQuantity.HasValue)
        {
            DomainException.ThrowIf(ScrapQuantity.Value < 0,
                "Fire miktarı negatif olamaz.");
            DomainException.ThrowIf(ActivityType != WorkOrderActivityType.Production,
                "Fire yalnız 'Üretim' tipinde girilebilir.");
        }
    }
}
