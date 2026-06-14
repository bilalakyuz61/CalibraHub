using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class MaterialCardDynamicFieldDefinition : Entity
{
    public string ScreenCode { get; init; } = "MaterialCards";
    /// <summary>
    /// Multi-layer ekranlar icin katman anahtari (orn. sales_quotes: "header" / "line").
    /// NULL veya "default" → tek katmanli ekran.
    /// </summary>
    public string? LayerKey { get; init; }
    public Guid? GroupId { get; init; }
    public required string FieldKey { get; init; }
    public required string FieldLabel { get; init; }
    public MaterialCardDynamicFieldDataType DataType { get; init; }
    public bool IsVisible { get; init; } = true;
    public bool IsRequired { get; init; }
    public string? DefaultValue { get; init; }
    public int DisplayOrder { get; init; }
    public int ColumnSpan { get; init; } = 1;
    public bool IsSystem { get; init; }
    public bool IsActive { get; private set; } = true;
    public DateTime Created { get; init; } = DateTime.Now;
    public DateTime Updated { get; private set; } = DateTime.Now;

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        Updated = DateTime.Now;
    }
}
