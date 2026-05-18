namespace CalibraHub.Web.Models.Definitions;

public sealed class CardGroupsViewModel
{
    public object? BoardConfig { get; init; }
}

public sealed class CardGroupEditViewModel
{
    public int?    Id          { get; init; }
    public int     CardType    { get; init; }
    public int     Level       { get; init; }
    public int?    ParentId    { get; init; }
    public string? ParentCode  { get; init; }
    public string? Code        { get; init; }
    public string? Description { get; init; }
}
