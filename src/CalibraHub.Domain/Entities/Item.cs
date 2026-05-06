using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Stok/malzeme kartlari. DocumentLine.ItemId, PriceList.ItemId ve stok-konfigurasyon mapping tablolari bu tabloya FK ile baglidir. Combinations = urun konfigurasyon ozelliklerinin acik oldugunu belirtir.")]
public sealed class Item
{
    public int Id { get; init; }
    public int CompanyId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public int? TypeId { get; init; }
    public int? UnitId { get; init; }
    public bool Combinations { get; init; } = false;
    public decimal TaxRate { get; init; } = 20m;
    public bool IsActive { get; private set; } = true;
    public DateTime? CreateDate { get; init; }
    public DateTime? ModifyDate { get; init; }

    public void Deactivate()
    {
        IsActive = false;
    }
}
