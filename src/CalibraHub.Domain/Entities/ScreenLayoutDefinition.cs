using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class ScreenLayoutDefinition : Entity
{
    public required string ScreenCode { get; init; }
    public required string LayoutJson { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; private set; } = DateTime.Now;

    public void UpdateLayout(string layoutJson)
    {
        LayoutJson = layoutJson;
        UpdatedAt = DateTime.Now;
    }
}
