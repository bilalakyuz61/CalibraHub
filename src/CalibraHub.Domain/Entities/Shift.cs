using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

[Description("Üretim vardiyası (2026-05-21 — Faz 3). Sabit zaman dilimi: Gündüz 07-15, Akşam 15-23, Gece 23-07 gibi. EndTime < StartTime ise IsOvernight=true (00:00 sınırını geçer). Personel atamaları ShiftAssignment tablosunda haftalık tekrar pattern'i ile tutulur. UI rozetleri ve raporlama grafikleri ColorHex'i kullanır.")]
public sealed class Shift
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public TimeSpan StartTime { get; init; }
    public TimeSpan EndTime { get; init; }
    /// <summary>EndTime &lt; StartTime ise true — gece vardiyası (00:00'ı geçer).</summary>
    public bool IsOvernight { get; init; }
    public string? ColorHex { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;

    // Audit
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }

    public void EnsureValid()
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(Code),
            "Vardiya kodu zorunludur.");
        DomainException.ThrowIf(Code.Length > 50,
            "Vardiya kodu en fazla 50 karakter olabilir.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(Name),
            "Vardiya adı zorunludur.");
        DomainException.ThrowIf(Name.Length > 200,
            "Vardiya adı en fazla 200 karakter olabilir.");
        DomainException.ThrowIf(StartTime == EndTime,
            "Başlangıç ve bitiş saatleri aynı olamaz (sıfır süreli vardiya geçersiz).");
        if (!string.IsNullOrWhiteSpace(ColorHex))
        {
            var c = ColorHex.Trim();
            DomainException.ThrowIf(!c.StartsWith('#') || (c.Length != 7 && c.Length != 9),
                "Renk kodu '#RRGGBB' veya '#RRGGBBAA' formatında olmalıdır.");
        }
    }

    /// <summary>
    /// Vardiya gece vardiyası mı? (EndTime &lt; StartTime → 00:00'ı geçer).
    /// IsOvernight property'sini override etmez — caller compute eder ve property'ye yazar.
    /// </summary>
    public static bool ComputeOvernight(TimeSpan start, TimeSpan end) => end < start;

    /// <summary>
    /// Süresi (TimeSpan). Gece vardiyası ise 24sa toplamı, normal vardiyada basit fark.
    /// </summary>
    public TimeSpan Duration =>
        IsOvernight
            ? (TimeSpan.FromHours(24) - StartTime + EndTime)
            : (EndTime - StartTime);
}

[Description("Vardiya içi mola (çay/yemek). 2026-05-21 — Faz 3. Net çalışma süresi = vardiya süresi − Σ ara. Mola her zaman vardiya saat aralığı içinde olmalı (gece vardiyası için day-cross hesap caller sorumluluğunda). NOT: IsPaid kolonu 2026-06-06 itibarıyla kullanım dışı — backward-compat için DB'de duruyor, kod yolundan çıkarıldı.")]
public sealed class ShiftBreak
{
    public int Id { get; init; }
    public int ShiftId { get; init; }
    public string Name { get; init; } = string.Empty;
    public TimeSpan StartTime { get; init; }
    public TimeSpan EndTime { get; init; }
    public int SortOrder { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;

    public TimeSpan Duration => EndTime - StartTime;

    public void EnsureValid()
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(Name),
            "Mola adı zorunludur (örn. 'Çay', 'Yemek').");
        DomainException.ThrowIf(Name.Length > 100,
            "Mola adı en fazla 100 karakter olabilir.");
        DomainException.ThrowIf(EndTime <= StartTime,
            $"Mola bitiş saati başlangıçtan sonra olmalı ({Name}: {StartTime}-{EndTime}).");
    }
}

[Description("Personel × Vardiya × Gün eşleştirmesi (2026-05-21 — Faz 3). Haftalık tekrar pattern; DayOfWeek 0=Pazar..6=Cumartesi. EffectiveFrom/To opsiyonel (geçici atama). Filtered unique: aynı personelin aynı günü aktif sadece bir vardiyaya bağlı olabilir.")]
public sealed class ShiftAssignment
{
    public int Id { get; init; }
    public int PersonnelId { get; init; }
    public int ShiftId { get; init; }
    public DayOfWeek DayOfWeek { get; init; }
    public DateOnly? EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public bool IsActive { get; init; } = true;

    // Audit
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }

    public void EnsureValid()
    {
        DomainException.ThrowIf(PersonnelId <= 0, "Personel seçilmelidir.");
        DomainException.ThrowIf(ShiftId <= 0,     "Vardiya seçilmelidir.");
        if (EffectiveFrom.HasValue && EffectiveTo.HasValue)
            DomainException.ThrowIf(EffectiveFrom.Value > EffectiveTo.Value,
                "Geçerlilik başlangıç tarihi, bitiş tarihinden sonra olamaz.");
    }
}
