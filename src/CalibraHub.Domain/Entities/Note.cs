using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class Note : Entity
{
    public int CompanyId { get; init; }
    public Guid UserId { get; init; }
    public Guid? FolderId { get; set; }
    public required string Title { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsDeleted { get; private set; }

    public void MarkDeleted() => IsDeleted = true;
}
