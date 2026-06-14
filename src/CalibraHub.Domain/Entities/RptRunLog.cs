namespace CalibraHub.Domain.Entities;

public sealed class RptRunLog
{
    public long Id { get; set; }
    public int? DefId { get; set; }
    public int ViewId { get; set; }
    public int UserId { get; set; }
    public int? CompanyId { get; set; }
    public DateTime StartedAt { get; set; }
    public int? DurationMs { get; set; }
    public int? RowCount { get; set; }
    public string? Error { get; set; }
    public byte[]? SqlHash { get; set; }
}
