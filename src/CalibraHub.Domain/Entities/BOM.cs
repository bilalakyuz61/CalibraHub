using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Urun agaci baslik (Bill of Materials). ParentMaterialCode ile Item tablosuna, ConfigurationCode ile varyanta baglanir. Bilesenleri BOMLine tablosunda 1-N iliskiyle tutulur.")]
public class BOM
{
    public int Id { get; init; }
    public string ParentMaterialCode { get; init; } = default!;
    public string? ConfigurationCode { get; init; }
    public string? Description { get; init; }
    public byte[]? ImageData { get; init; }
    public string? ImageMimeType { get; init; }
    public string? ImageFitMode { get; init; }

    public ICollection<BOMLine> Lines { get; init; } = new List<BOMLine>();
}
