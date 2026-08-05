using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

[Description("Vardiya Senaryosu (2026-08-05) — işletmenin farklı çalışma düzenlerini (tek vardiya/çift vardiya vb.) isimlendirilmiş senaryolar olarak tanımlar. Makine çalışma takvimi artık serbest pencere yerine bu senaryo altındaki ScenarioMachineShift atamalarından türetilir (bkz. SqlMachineCalendarRepository.ListWorkWindowsAsync). IsDefault=1 olan senaryo, scenarioId verilmeden yapılan sorgularda kullanılır (tek varsayılan — UX_ShiftScenario_Default filtered unique).")]
public sealed class ShiftScenario
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; } = true;

    // Audit
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }

    public void EnsureValid()
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(Name),
            "Senaryo adı zorunludur.");
        DomainException.ThrowIf(Name.Length > 200,
            "Senaryo adı en fazla 200 karakter olabilir.");
        DomainException.ThrowIf(Description is { Length: > 500 },
            "Açıklama en fazla 500 karakter olabilir.");
    }
}

[Description("Senaryo × Makine × Vardiya × Gün eşleştirmesi (2026-08-05, TEK mapping — ayrı ScenarioShift tablosu elenmiştir). Makine çalışma penceresi bu tablodan türetilir: DaysMask'in set-bit'i olan her gün için Shift.StartTime/EndTime (IsOvernight ise iki güne bölünür: (start,1440)@gün + (0,end)@gün+1) MachineWorkWindowDto'ya dönüştürülür. DaysMask bit0=Pazar..bit6=Cumartesi (MachineWorkWindowDto.DayOfWeek ile aynı konvansiyon).")]
public sealed class ScenarioMachineShift
{
    public int Id { get; init; }
    public int ScenarioId { get; init; }
    public int MachineId { get; init; }
    public int ShiftId { get; init; }

    /// <summary>bit0=Pazar..bit6=Cumartesi (0..127; bit7 kullanılmaz).</summary>
    public byte DaysMask { get; init; }
    public bool IsActive { get; init; } = true;

    // Audit
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }

    public void EnsureValid()
    {
        DomainException.ThrowIf(ScenarioId <= 0, "Senaryo seçilmelidir.");
        DomainException.ThrowIf(MachineId <= 0, "Makine seçilmelidir.");
        DomainException.ThrowIf(ShiftId <= 0, "Vardiya seçilmelidir.");
        DomainException.ThrowIf(DaysMask > 127, "Geçersiz gün maskesi (0-127 aralığında olmalı).");
    }
}
