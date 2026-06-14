namespace CalibraHub.Domain.Entities;

public sealed class ReportDashboardAccess
{
    public int      Id                  { get; init; }
    public int      ReportDashboardId   { get; set; }
    public int?     UserId              { get; set; }
    public int?     DepartmentId        { get; set; }
    public string?  CreatedBy           { get; set; }
    public DateTime Created             { get; init; }
}
