namespace CalibraHub.Domain.Entities;

public sealed class ReportTemplate
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public int DocumentTypeId { get; init; }

    /// <summary>
    /// Eski veri uyumu icin tutuluyor (legacy: wwwroot/Document/Templates/...).
    /// Yeni sablonlar FrxContent ile DB'ye kaydedilir.
    /// </summary>
    public string? FrxFilePath { get; init; }

    /// <summary>
    /// .frx icerigi binary olarak DB'de tutulur. Yeni akisin ana depolama alani.
    /// </summary>
    public byte[]? FrxContent { get; set; }

    public string? Description { get; init; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
