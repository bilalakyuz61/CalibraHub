using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class FeatureValue : Entity
{
    public Guid PropertyId { get; init; }
    public required string Code { get; init; }
    public required string Description { get; init; }
    public required string Value { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; private set; } = DateTime.Now;

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.Now;
    }
}
