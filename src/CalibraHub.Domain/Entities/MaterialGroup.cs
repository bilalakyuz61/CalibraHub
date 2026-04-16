namespace CalibraHub.Domain.Entities;

public sealed class MaterialGroup
{
    public int Id { get; init; }
    public int GroupCategory { get; init; }
    public required string GroupCode { get; init; }
    public string? GroupDescription { get; init; }
}
