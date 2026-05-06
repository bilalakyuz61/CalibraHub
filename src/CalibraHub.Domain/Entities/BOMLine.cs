using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Urun agaci bilesenleri. BOMId FK -> BOM.Id (baslik). ItemId FK -> Items.id (bilesen malzemesi). ConfigId FK -> ItemConfiguration.Id (varyant). Quantity = 1 birim parent icin gerekli bilesen adedi; ScrapRatio = fire orani.")]
public class BOMLine
{
    public int Id { get; init; }
    public int BOMId { get; init; }
    public int ItemId { get; init; }
    public int? ConfigId { get; init; }
    public decimal Quantity { get; init; } = 1;
    public decimal ScrapRatio { get; init; } = 0;
    public Guid LineGuid { get; init; }
}
