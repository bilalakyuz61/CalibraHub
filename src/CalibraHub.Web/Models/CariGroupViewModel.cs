namespace CalibraHub.Web.Models;

public sealed class CariGroupSmartBoardViewModel
{
    public object? BoardConfig { get; init; }
}

public sealed class CariGroupEditViewModel
{
    public int?   Id            { get; init; }
    public string Name          { get; init; } = "";
    public int    SortOrder     { get; init; } = 0;
    public bool   IsActive      { get; init; } = true;
    public int    GroupCategory { get; init; } = 1;
}

public sealed class CariGroupInput
{
    public int?   Id            { get; set; }
    public string Name          { get; set; } = "";
    public int    SortOrder     { get; set; } = 0;
    public bool   IsActive      { get; set; } = true;
    public int    GroupCategory { get; set; } = 1;
}
