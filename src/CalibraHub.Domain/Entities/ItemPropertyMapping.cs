using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class ItemPropertyMapping : Entity
{
    public int ItemId { get; init; }
    public Guid PropertyId { get; init; }
    public Guid? PropertyValueId { get; init; }
    public string? ConfigurationCode { get; init; }
    public string? TextValue { get; init; }
    public decimal? NumericValue { get; init; }
    public DateTime? DateValue { get; init; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; private set; } = DateTime.Now;

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.Now;
    }
}
