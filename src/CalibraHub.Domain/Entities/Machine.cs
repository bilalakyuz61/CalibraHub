using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Uretim/depo makineleri. Her makine bir Location'a (uretim hatti, depo lokasyonu) baglidir. Iletim emri / iş emri rotalama, kapasite planlama, OEE hesabi makineye bagli atamalardan beslenir.")]
public sealed class Machine
{
    public int Id { get; init; }

    /// <summary>Sahip sirket — multi-tenant icin gerekli (per-company DB olsa bile tutulur).</summary>
    public int CompanyId { get; init; }

    /// <summary>FK Location.Id — makinenin bulundugu fiziki lokasyon.</summary>
    public int LocationId { get; init; }

    /// <summary>Kisa benzersiz makine kodu (UNIQUE per company).</summary>
    public required string MachineCode { get; init; }

    /// <summary>Makinenin gorunur adi (ornek: "CNC-1 Torna").</summary>
    public string? MachineName { get; init; }

    /// <summary>Saatlik max uretim kapasitesi (mamul birimi cinsinden).</summary>
    public decimal? HourlyCapacity { get; init; }

    /// <summary>Goruntu siralama.</summary>
    public int SortOrder { get; init; }

    /// <summary>Pasife alinan makine planlama listelerinde gozukmez.</summary>
    public bool IsActive { get; init; } = true;
}
