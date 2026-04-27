using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Urun agaci bilesenleri. BOMId FK -> BOM.Id (baslik). Quantity = 1 birim parent icin gerekli bilesen adedi; ScrapRatio = fire orani. ComponentConfigCode ile konfigurasyonlu bilesen destegi.")]
public class BOMLine
{
    public int Id { get; init; }
    public int BOMId { get; init; }
    public string ComponentMaterialCode { get; init; } = default!;
    public string? ComponentConfigCode { get; init; }
    public decimal Quantity { get; init; } = 1;
    public decimal ScrapRatio { get; init; } = 0;
    public Guid LineGuid { get; init; }
}
