using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("Ozellik master tanimlari (Renk, Boyut, Beden vb.). Urun konfigurasyonu icin kullanilir; ozelliklerin secilebilir degerleri FeatureValue tablosunda tutulur. CompanyId ile sirket-bazli filtre yapilir.")]
public sealed class ItemFeature
{
    public int Id { get; init; }
    public int CompanyId { get; init; }
    public required string Name { get; init; }
    public ConfigurationFieldDataType DataType { get; init; }
    public string? UnitOfMeasure { get; init; }
    public bool VisibleInDesign { get; init; } = true;
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; private set; } = DateTime.Now;

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.Now;
    }
}
