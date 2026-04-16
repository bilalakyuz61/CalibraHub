using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Models.Shared;

public class GridListStateViewModel
{
    public string GridKey { get; init; } = string.Empty;
    public string SearchTerm { get; init; } = string.Empty;
    public string SortBy { get; init; } = string.Empty;
    public string SortDirection { get; init; } = "asc";
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    public string PageParameterName { get; init; } = "page";
    public string PageSizeParameterName { get; init; } = "pageSize";
    public string ItemLabel { get; init; } = "kayit";
    public IReadOnlyCollection<SelectListItem> PageSizeOptions { get; init; } = Array.Empty<SelectListItem>();

    public int StartRow => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;
    public int EndRow => TotalCount == 0 ? 0 : Math.Min(Page * PageSize, TotalCount);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}

public sealed class GridPagerViewModel
{
    public GridListStateViewModel State { get; init; } = new GridListStateViewModel();
    public string ActionName { get; init; } = string.Empty;
    public string? ControllerName { get; init; }
    public IReadOnlyDictionary<string, string?> RouteValues { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}
