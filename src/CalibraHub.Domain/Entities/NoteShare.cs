using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class NoteShare : Entity
{
    public Guid NoteId { get; init; }
    public Guid SharedWithUserId { get; init; }
    public DateTime SharedAt { get; init; } = DateTime.Now;
}
