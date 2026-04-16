namespace CalibraHub.Domain.Entities;

public class ProductTree
{
    public int Id { get; init; }
    public string ParentMaterialCode { get; init; } = default!;
    public string? ConfigurationCode { get; init; }
    public string? Description { get; init; }
    public byte[]? ImageData { get; init; }
    public string? ImageMimeType { get; init; }
    public string? ImageFitMode { get; init; }

    public ICollection<ProductTreeLine> Lines { get; init; } = new List<ProductTreeLine>();
}
