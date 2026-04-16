using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class NoteAttachment : Entity
{
    public Guid NoteId { get; init; }
    public required string FileName { get; init; }
    public required string StoredName { get; init; }
    public string? ContentType { get; init; }
    public long FileSize { get; init; }
    public DateTime UploadedAt { get; init; } = DateTime.Now;
    public string? Description { get; set; }
}
