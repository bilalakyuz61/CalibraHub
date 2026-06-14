using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// AR-GE projesi prototip/cikti kaydi. ProjectId -> Document.Id (arge_proje belgesi).
/// Prototip henuz bir stok kartina baglanmamis olabilir (ItemId nullable) — uretime
/// gecis koprusu sirasinda yeni seri Item turetilip baglanir.
/// </summary>
[Description("AR-GE prototip kaydi. ProjectId -> Document.Id. ItemId nullable (kopru oncesi henuz stok karti yok).")]
public sealed class ArgePrototype
{
    public int Id { get; init; }

    [Description("Bagli proje belgesi. FK -> Document.Id.")]
    public int ProjectId { get; init; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    [Description("Versiyon etiketi (v0.1, MVP-2).")]
    public string? VersionLabel { get; set; }

    [Description("Prototip stok kartina baglandiysa. FK -> Items.Id (nullable).")]
    public int? ItemId { get; set; }

    public bool IsApproved { get; set; }

    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
