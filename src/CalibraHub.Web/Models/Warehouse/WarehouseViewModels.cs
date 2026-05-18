namespace CalibraHub.Web.Models.Warehouse;

public sealed class WarehouseBoardViewModel
{
    public object? BoardConfig { get; init; }
}

public sealed class StockDocEditViewModel
{
    public int? DocId { get; init; }
    public string DocType { get; init; } = "";
    public string LineGridConfigJson { get; init; } = "null";
}
