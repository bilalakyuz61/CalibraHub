using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

[Description("Dinamik alan gruplari (widget/EAV altyapisi). Ekran tabanli (ScreenCode) sekmeler/katmanlar. Field tablosu bu grupla eslesir.")]
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
    public DateTime Created { get; init; } = DateTime.Now;
    public DateTime Updated { get; private set; } = DateTime.Now;

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        Updated = DateTime.Now;
    }
}
