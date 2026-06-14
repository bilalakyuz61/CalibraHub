using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class NoteShare : Entity
{
    public Guid NoteId { get; init; }
    public int SharedWithUserId { get; init; }
    public DateTime SharedAt { get; init; } = DateTime.Now;
    public bool CanEdit { get; init; }
}
