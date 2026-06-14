namespace CalibraHub.Domain.Entities;

public sealed class ReportDashboard
{
    public int    Id          { get; init; }
    public string GrafanaUid  { get; set; } = string.Empty;
    public string Title       { get; set; } = string.Empty;
    public string? FolderTitle { get; set; }
    public string? Tags        { get; set; }  // JSON: ["tag1","tag2"]
    public int    SortOrder   { get; set; }
    public bool   IsActive    { get; set; } = true;
    public string? CreatedBy  { get; set; }
    public DateTime Created   { get; init; }
    public string? UpdatedBy  { get; set; }
    public DateTime? Updated  { get; set; }
}
