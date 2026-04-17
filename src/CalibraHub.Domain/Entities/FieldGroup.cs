using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class FieldGroup : Entity
{
    public string ScreenCode { get; init; } = "MaterialCards";
    /// <summary>
    /// Multi-layer ekranlar icin katman anahtari. NULL → tek katman.
    /// </summary>
    public string? LayerKey { get; init; }
    public required string GroupKey { get; init; }
    public required string GroupLabel { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; private set; } = DateTime.Now;

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        UpdatedAt = DateTime.Now;
    }
}
