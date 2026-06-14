namespace CalibraHub.Domain.Entities;

/// <summary>
/// Calibra master DB'deki merkezi ek tablosu.
/// EntityType + EntityId polimorfik tasarim — not, teklif, uretim recetesi vb.
/// tum entity turleri bu tek tabloda ek saklayabilir.
/// Diger DB'lerden SYNONYM uzerinden erisim saglanir.
/// </summary>
public sealed class Attachment
{
    public int     Id            { get; set; }
    public required string EntityType  { get; init; }
    public required string EntityId    { get; init; }
    public required string FileName    { get; init; }
    public string? ContentType   { get; init; }
    public long    FileSize      { get; init; }
    public string? Description   { get; set; }
    public bool    IsActive      { get; set; } = true;
    public int?    CreatedById   { get; init; }
    public DateTime Created      { get; init; } = DateTime.UtcNow;
    public int?    UpdatedById   { get; set; }
    public DateTime? Updated     { get; set; }

    /// <summary>
    /// Liste sorgularinda NULL gelir — yalnizca download path'inde doldurulur.
    /// </summary>
    public byte[]? BinaryContent { get; set; }
}
