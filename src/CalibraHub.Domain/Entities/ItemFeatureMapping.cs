namespace CalibraHub.Domain.Entities;

public sealed class ItemFeatureMapping
{
    public int Id { get; init; }
    public int ItemId { get; init; }
    public int FeatureId { get; init; }
    public int? FeatureValueId { get; init; }
    public bool PrintDescriptionInDesign { get; init; } = true;
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; private set; } = DateTime.Now;

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.Now;
    }
}
