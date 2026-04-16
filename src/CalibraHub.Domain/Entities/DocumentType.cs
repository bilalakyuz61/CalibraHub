using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class DocumentType : Entity
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? SqlViewName { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
