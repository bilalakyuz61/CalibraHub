namespace CalibraHub.Domain.Entities;

public sealed class DocLayoutDs
{
    public int Id { get; set; }
    public int LayoutId { get; set; }
    public required string Alias { get; set; }
    public required string Role { get; set; }  // "master" | "detail" | "subdetail"
    public int? ViewId { get; set; }
    public string? AdHocSql { get; set; }
    public string? JoinOn { get; set; }
    public string? ParentAlias { get; set; }
    public int Ordinal { get; set; }
}
