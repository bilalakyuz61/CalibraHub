using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir <see cref="CodeRule"/>'un anlık sayaç state'i. ResetKey ile dönem ayırılır:
///   - ResetPeriod=Yearly  → ResetKey="2026"
///   - ResetPeriod=Monthly → ResetKey="202606"
///   - ResetPeriod=Daily   → ResetKey="20260621"
///   - ResetPeriod=None    → ResetKey="" (tek satır, sürekli artar)
///
/// UNIQUE INDEX (RuleId + ResetKey) — concurrent insert'te güvenli increment için
/// UPDATE OUTPUT pattern'i kullanılır (CodeGeneratorService).
/// </summary>
[Description("Cari/Stok kod kuralı sayaç state'i — kural başına ve dönem başına son değer.")]
public sealed class CodeRuleCounter
{
    public int Id { get; init; }
    public int RuleId { get; set; }
    public string ResetKey { get; set; } = string.Empty;
    public long CurrentValue { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
