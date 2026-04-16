namespace CalibraHub.Domain.Entities;

public sealed class ProductConfigurationRecord
{
    public int Id { get; init; }
    public int? ParentId { get; init; }
    public required string RecordType { get; init; }
    public required string RecordCode { get; init; }
    public required string RecordName { get; init; }
    public string? DataType { get; init; }
    public string? RelatedMaterialCode { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedDate { get; init; }
}
