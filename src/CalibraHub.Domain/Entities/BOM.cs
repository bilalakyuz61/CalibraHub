using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Urun agaci baslik (Bill of Materials). ItemId FK ile Items tablosuna, ConfigId FK ile ItemConfiguration varyantina baglanir. Bilesenleri BOMLine tablosunda 1-N iliskiyle tutulur. ImageRotation: gorselin gosterim donus acisi (0/90/180/270 derece).")]
public class BOM
{
    public int Id { get; init; }
    public int ItemId { get; init; }
    public int? ConfigId { get; init; }
    public string? Description { get; init; }
    public byte[]? ImageData { get; init; }
    public string? ImageMimeType { get; init; }
    public string? ImageFitMode { get; init; }
    public int ImageRotation { get; init; } = 0;

    public ICollection<BOMLine> Lines { get; init; } = new List<BOMLine>();
}
