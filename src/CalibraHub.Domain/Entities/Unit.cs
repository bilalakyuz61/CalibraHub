using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Olcu birimi tanimlari (adet, kg, metre, litre vb). Item ve DocumentLine bu koddan birim ismi cozer. IntlCode = ISO/UNECE uluslararasi kod.")]
public sealed class Unit
{
    public int Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? IntlCode { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; }
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }
}
