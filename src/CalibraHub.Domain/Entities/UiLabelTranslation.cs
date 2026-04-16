using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class UiLabelTranslation : Entity
{
    public required string FormKey { get; init; }
    public required string LabelKey { get; init; }
    public required string LanguageCode { get; init; }
    public required string LabelText { get; init; }
    public DateTime UpdatedAt { get; init; } = DateTime.Now;
}
