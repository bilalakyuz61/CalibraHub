namespace CalibraHub.Web.Models.Shared;

public sealed class ScreenLayoutTabViewModel
{
    public string TabKey { get; init; } = string.Empty;
    public string TabLabel { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
    public IReadOnlyCollection<ScreenLayoutItemViewModel> Items { get; init; } = Array.Empty<ScreenLayoutItemViewModel>();
}

public sealed class ScreenLayoutItemViewModel
{
    public string ItemKey { get; init; } = string.Empty;
    public string ItemLabel { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
    public int ColumnSpan { get; init; } = 1;
    public bool IsVisible { get; init; } = true;
    public bool IsRequired { get; init; }
}
