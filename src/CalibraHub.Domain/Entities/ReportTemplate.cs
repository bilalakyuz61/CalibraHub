using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class ReportTemplate : Entity
{
    public required string Name { get; init; }
    public Guid DocumentTypeId { get; init; }
    public string? FrxFilePath { get; init; }
    public string? Description { get; init; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
