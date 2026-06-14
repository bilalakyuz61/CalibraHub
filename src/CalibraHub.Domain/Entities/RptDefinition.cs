using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class RptDefinition
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int ViewId { get; set; }
    public ReportCategory Category { get; set; }
    public required string ConfigJson { get; set; }
    public int OwnerUserId { get; set; }
    public bool IsShared { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
