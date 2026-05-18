using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir DocumentNumberRule'un anlık sayaç state'i. ResetKey ile dönem ayırılır:
///   - ResetPeriod=Yearly  → ResetKey="2026"
///   - ResetPeriod=Monthly → ResetKey="2026-05"
///   - ResetPeriod=Daily   → ResetKey="2026-05-15"
///   - ResetPeriod=None    → ResetKey="" (tek satır, sürekli artar)
///
/// UNIQUE INDEX (RuleId + ResetKey) — concurrent insert'te güvenli increment için
/// SP/lock ile UPDATE @CurrentValue = @CurrentValue + 1 → OUTPUT inserted.CurrentValue.
/// </summary>
[Description("Belge numarası sayaç state'i — kural başına ve dönem başına son kullanılan değer.")]
public sealed class DocumentNumberCounter
{
    public int Id { get; init; }

    /// <summary>FK -> DocumentNumberRule.Id (CASCADE delete).</summary>
    public int RuleId { get; set; }

    /// <summary>
    /// Sıfırlama anahtarı (dönem). ResetPeriod None ise boş string.
    ///   Yearly  → "2026"
    ///   Monthly → "2026-05"
    ///   Daily   → "2026-05-15"
    /// </summary>
    public string ResetKey { get; set; } = string.Empty;

    /// <summary>Son kullanılan sayaç değeri. İlk insert'te CounterStart - 1 olarak set edilir.</summary>
    public long CurrentValue { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
