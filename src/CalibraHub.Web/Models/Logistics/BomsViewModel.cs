using CalibraHub.Web.Models.Shared;

namespace CalibraHub.Web.Models.Logistics;

public sealed class BomsViewModel
{
    public required IReadOnlyCollection<BomRowViewModel> Boms { get; init; }
    public required BomListStateViewModel ListState { get; init; }
    public IReadOnlyCollection<GridColumnDefinition> AvailableColumns { get; init; } = [];
    public IReadOnlyCollection<string> VisibleColumns { get; init; } = [];
    public object? BoardConfig { get; init; }
}

public sealed class BomRowViewModel
{
    public int Id { get; init; }
    public int ParentItemId { get; init; }
    public string ParentItemCode { get; init; } = string.Empty;
    public string ParentItemName { get; init; } = string.Empty;
    public int? ConfigId { get; init; }
    public string? ConfigCode { get; init; }
    public int LineCount { get; init; }
    public string? Description { get; init; }
    public bool HasImage { get; init; }
}

public sealed class BomListStateViewModel : GridListStateViewModel
{
}
