namespace CalibraHub.Domain.Entities;

/// <summary>
/// Calibra master DB'deki merkezi ek tablosu.
/// FormId + RefId polimorfik tasarım — Varlık, Serbest Belge vb. tüm entity türleri
/// bu tek tabloda ek saklayabilir. Not ekleri company DB'deki note_attachments tablosunda tutulur.
/// </summary>
public sealed class Attachment
{
    public int Id                { get; set; }
    public int FormId            { get; init; }
    public int RefId             { get; init; }
    public string? Title         { get; set; }
    public string? Category      { get; set; }
    public string? Tags          { get; set; }
    public required string FileName   { get; init; }
    public string? ContentType   { get; init; }
    public long FileSize         { get; init; }
    public string? Description   { get; set; }
    public short RevisionNumber  { get; set; } = 1;
    public int? OriginalId       { get; set; }
    public bool IsActive         { get; set; } = true;
    public int? CreatedById      { get; init; }
    public DateTime Created      { get; init; } = DateTime.UtcNow;
    public int? UpdatedById      { get; set; }
    public DateTime? Updated     { get; set; }

    /// <summary>
    /// Liste sorgularında NULL gelir — yalnızca download path'inde doldurulur.
    /// </summary>
    public byte[]? BinaryContent { get; set; }
}
