using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("ItemFeature tablosundaki ozelliklere ait secilebilir degerler (ornegin 'Renk' ozelligi icin Kirmizi/Mavi/Yesil). PropertyId FK -> ItemFeature.Id (INT).")]
public sealed class FeatureValue
{
    public int Id { get; init; }
    public int PropertyId { get; init; }
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
