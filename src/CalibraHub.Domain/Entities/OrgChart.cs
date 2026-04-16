using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class OrgChart : Entity
{
    public int CompanyId { get; init; }
    public required string Name { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
