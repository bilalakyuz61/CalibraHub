using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Models.Logistics;

public sealed class ProductCombinationsViewModel
{
    public required IReadOnlyCollection<SelectListItem> StockCodeOptions { get; init; }
    public string? SelectedStockCode { get; init; }
    public int? SelectedStockId { get; init; }
    public string? SelectedStockName { get; init; }

    public int SelectedStockIntId => SelectedStockId ?? 0;

    public IReadOnlyCollection<CombinationFeatureVm> LinkedFeatures { get; init; } = Array.Empty<CombinationFeatureVm>();
    public IReadOnlyCollection<CombinationRowVm> Combinations { get; init; } = Array.Empty<CombinationRowVm>();
}

public sealed class CombinationFeatureVm
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public IReadOnlyList<CombinationValueVm> Values { get; init; } = Array.Empty<CombinationValueVm>();
}

public sealed class CombinationValueVm
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public bool IsSelected { get; init; }
}

public sealed class CombinationCellVm
{
    public int FeatureId { get; init; }
    public string FeatureCode { get; init; } = string.Empty;
    public string FeatureName { get; init; } = string.Empty;
    public int ValueId { get; init; }
    public string ValueCode { get; init; } = string.Empty;
    public string ValueDescription { get; init; } = string.Empty;
}

public sealed class CombinationRowVm
{
    public int Id { get; init; }
    public int Index { get; init; }
    public IReadOnlyList<CombinationCellVm> Cells { get; init; } = Array.Empty<CombinationCellVm>();
    public string CombinedCode { get; init; } = string.Empty;
    public string CombinedDescription { get; init; } = string.Empty;
    public bool IsSelected { get; init; }
}
