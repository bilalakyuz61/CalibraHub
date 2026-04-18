using System.ComponentModel;
using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("EAV (Entity-Attribute-Value) tasarim seviyesi ozellik/nitelik tanimlari. Urun konfigurasyonu (renk, boyut, beden vb.) icin kullanilir; ozelliklerin secilebilir degerleri FeatureValue tablosunda tutulur.")]
public sealed class Feature : Entity
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public ConfigurationFieldDataType DataType { get; init; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; private set; } = DateTime.Now;

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.Now;
    }
}
