namespace CalibraHub.Domain.Entities;

public sealed class DocumentLineDetail
{
    public int Id { get; init; }
    public int QuoteLineId { get; init; }
    public string FeatureName { get; set; } = string.Empty;
    public string ValueCode { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int LineOrder { get; set; }
}
