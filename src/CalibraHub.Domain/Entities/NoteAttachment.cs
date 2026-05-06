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

    /// <summary>
    /// Dosya icerigi DB'de varbinary(max) olarak saklanir. Liste sorgularinda
    /// ekstra IO yapmamak icin kapali geliyor (default null); yalnizca
    /// IndirseAttachmentBinary cagirisinda doldurulur. Yeni upload'lar dogrudan
    /// buraya yazilir; legacy attachment'lar (binary null) icin
    /// NotesController.DownloadAttachment file system fallback yapar.
    /// </summary>
    public byte[]? BinaryContent { get; set; }
}
