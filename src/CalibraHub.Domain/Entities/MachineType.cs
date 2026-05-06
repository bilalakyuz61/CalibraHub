using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Makine tipi referans verisi. Logo Netsis Ileri Uretim Planlama dokumanindan turetilen 9 standart tip + admin tarafindan eklenebilen ozel tipler. Machine.MachineType bu tablonun [Code] kolonuna referans verir.")]
public sealed class MachineType
{
    public int Id { get; init; }

    /// <summary>Kisa kod (PK olarak kullanilir, UNIQUE). Ornek: NORMAL_SINGLE, FASON, ROBOT.</summary>
    public required string Code { get; init; }

    /// <summary>UI'da gorunen tam ad. Ornek: "Normal (Single Processor)".</summary>
    public required string Name { get; init; }

    /// <summary>Tipi karakterize eden uzun aciklama (planlama davranisi vs.).</summary>
    public string? Description { get; init; }

    /// <summary>Built-in (sistem tarafindan eklenen 9 standart tip) ise true. Built-in tipler silinmez.</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>Goruntu siralamasi.</summary>
    public int SortOrder { get; init; }

    public bool IsActive { get; init; } = true;
}
