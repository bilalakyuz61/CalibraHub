namespace CalibraHub.Domain.Entities;

public sealed class Unit
{
    public int Id { get; init; }
    public required string UnitCode { get; init; }
    public required string UnitName { get; init; }
    public string? IntlCode { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}
