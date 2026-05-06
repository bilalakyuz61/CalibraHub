namespace CalibraHub.Domain.Entities;

public sealed class ItemUnit
{
    public int Id { get; init; }
    public int ItemId { get; init; }
    public int LineNo { get; set; }
    public int UnitId { get; set; }
    public decimal Multiplier { get; set; }
}
