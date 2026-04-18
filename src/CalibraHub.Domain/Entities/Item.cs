using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Stok/malzeme kartlari. DocumentLine.ItemId, PriceList.ItemId ve stok-konfigurasyon mapping tablolari bu tabloya FK ile baglidir. TrackCombinations = urun konfigurasyon ozelliklerinin acik oldugunu belirtir.")]
public sealed class Item
{
    public int Id { get; init; }
    public required string MaterialCode { get; init; }
    public required string MaterialName { get; init; }
    public string? MaterialDescription { get; init; }
    public int? MaterialTypeId { get; init; }
    public bool TrackCombinations { get; init; } = false;
    public decimal TaxRate { get; init; } = 20m;
    public bool IsActive { get; private set; } = true;
    public DateTime? CreatedDate { get; init; }
    public int? CreatedByUserId { get; init; }
    public DateTime? ModifiedDate { get; init; }
    public int? ModifiedByUserId { get; init; }

    public byte[]? ImageData { get; init; }
    public string? ImageMimeType { get; init; }

    public void Deactivate()
    {
        IsActive = false;
    }
}
