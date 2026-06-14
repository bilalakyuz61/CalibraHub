using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// AR-GE test/deneme kaydi. ProjectId -> Document.Id, opsiyonel PrototypeId -> ArgePrototype.Id.
/// IsPassed NULL iken sonuc bekleniyor demektir.
/// </summary>
[Description("AR-GE test/deneme kaydi. ProjectId -> Document.Id, PrototypeId -> ArgePrototype.Id (nullable). Sonuc/olcum alanlari opsiyonel.")]
public sealed class ArgeTest
{
    public int Id { get; init; }

    [Description("Bagli proje belgesi. FK -> Document.Id.")]
    public int ProjectId { get; init; }

    [Description("Hangi prototip test edildi. FK -> ArgePrototype.Id (nullable).")]
    public int? PrototypeId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    [Description("Test/deneme tarihi.")]
    public DateTime? TestDate { get; set; }

    [Description("Olcum sonucu.")]
    public decimal? ResultValue { get; set; }

    [Description("Olcum birimi. FK -> Unit.Id (nullable).")]
    public int? ResultUnitId { get; set; }

    [Description("Test gecti mi? NULL = sonuc bekleniyor.")]
    public bool? IsPassed { get; set; }

    public string? ResultNotes { get; set; }

    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
