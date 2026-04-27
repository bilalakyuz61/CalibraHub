using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

[Description("Dinamik alan (EAV widget) tanimlari. FieldGroup uzerinden ekranlara baglanir; FieldKey runtime'da widget deger kolonuna karsilik gelir.")]
public sealed class Field : Entity
{
    public required string FieldKey { get; init; }
    public required string FieldLabel { get; init; }
    public bool IsVisible { get; init; } = true;
    public bool IsRequired { get; init; }
    public int DisplayOrder { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; init; } = DateTime.Now;
}
