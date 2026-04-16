using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class MaterialCardFieldOption : Entity
{
    public Guid FieldDefinitionId { get; init; }
    public required string OptionKey { get; init; }
    public required string OptionLabel { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; private set; } = DateTime.Now;

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        UpdatedAt = DateTime.Now;
    }
}
