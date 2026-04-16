namespace CalibraHub.Domain.Entities;

public sealed class StockUnitConversion
{
    public int Id { get; init; }
    public int StockCardId { get; init; }
    public int LineNo { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal Multiplier { get; set; }
}
