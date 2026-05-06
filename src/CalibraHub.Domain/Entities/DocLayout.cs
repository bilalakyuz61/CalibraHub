namespace CalibraHub.Domain.Entities;

public sealed class DocLayout
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string DocType { get; set; }
    public string? Description { get; set; }
    public required string LayoutJson { get; set; }
    public decimal PageW { get; set; } = 210m;
    public decimal PageH { get; set; } = 297m;
    public decimal MarginTop { get; set; } = 10m;
    public decimal MarginBot { get; set; } = 10m;
    public decimal MarginLeft { get; set; } = 15m;
    public decimal MarginRight { get; set; } = 10m;
    public Guid OwnerUserId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
