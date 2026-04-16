namespace CalibraHub.Domain.Entities;

public sealed class WarehouseLocation
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
}
