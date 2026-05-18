using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Depo ve raf lokasyonlari. Kendi uzerinde self-reference (ParentId) ile hiyerarsi: Depo > Kat > Kor > Raf > Goz. LocationTypeCode ile tip ayrimi.")]
public sealed class Location
{
    public int Id { get; init; }
    public int? ParentId { get; init; }
    public required string LocationTypeCode { get; init; }
    public required string LocationCode { get; init; }
    public string? LocationName { get; init; }
    public int SortOrder { get; init; }
    public decimal? MaxWeightCapacity { get; init; }
    public decimal? VolumeCapacity { get; init; }
    public bool IsActive { get; init; } = true;
    public bool IsMachinePark { get; init; }
    public bool IsStorageArea { get; init; }
}
