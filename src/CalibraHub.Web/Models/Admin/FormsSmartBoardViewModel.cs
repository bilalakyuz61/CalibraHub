namespace CalibraHub.Web.Models.Admin;

public sealed class FormsSmartBoardViewModel
{
    public object? BoardConfig { get; init; }
}

public sealed class FormEditViewModel
{
    public int?    Id             { get; init; }
    public string? FormCode       { get; init; }
    public string? FormName       { get; init; }
    public string? Module         { get; init; }
    public string? SubModule      { get; init; }
    public int     SortOrder      { get; init; }
    public bool    IsActive       { get; init; } = true;
    public string? BaseTable      { get; init; }
    public string? BaseRecordKey  { get; init; }
}
