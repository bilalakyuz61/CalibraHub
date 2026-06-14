using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class NoteFolder : Entity
{
    public int CompanyId { get; init; }
    public int UserId { get; init; }
    public required string Name { get; set; }
    public Guid? ParentFolderId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public bool IsDeleted { get; private set; }

    public void MarkDeleted() => IsDeleted = true;
}
