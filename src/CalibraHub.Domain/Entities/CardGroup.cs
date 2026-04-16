namespace CalibraHub.Domain.Entities;

public sealed class CardGroup
{
    public int Id { get; init; }
    /// <summary>1 = Malzeme, 2 = Cari</summary>
    public int CardType { get; init; }
    /// <summary>1–5</summary>
    public int Level { get; init; }
    public int? ParentId { get; init; }
    public required string Code { get; init; }
    public string? Description { get; init; }
}
