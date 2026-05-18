namespace CalibraHub.Web.Models.Logistics;

public sealed class MachinesSmartBoardViewModel
{
    public object? BoardConfig { get; init; }
}

public sealed class MachineEditViewModel
{
    public int?    Id          { get; init; }
    public int     LocationId  { get; init; }
    public string? MachineCode { get; init; }
    public string? MachineName { get; init; }
    public int     SortOrder   { get; init; }
    public bool    IsActive    { get; init; } = true;
}

public sealed class MaterialGroupsSmartBoardViewModel
{
    public object? BoardConfig { get; init; }
}

public sealed class MaterialGroupEditViewModel
{
    public int?    Id               { get; init; }
    public int     GroupCategory    { get; init; } = 1;
    public string? GroupCode        { get; init; }
    public string? GroupDescription { get; init; }
}

public sealed class CombinationsSmartBoardViewModel
{
    public object? BoardConfig { get; init; }
}
