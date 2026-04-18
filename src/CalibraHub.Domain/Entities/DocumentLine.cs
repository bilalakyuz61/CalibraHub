using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Document tablosundaki belgelerin satir detaylari (malzeme, adet, birim fiyat, iskonto, satir toplami). DocumentId FK ile basliga baglidir; ItemId FK ise stok kartina.")]
public sealed class DocumentLine
{
    [Description("Birincil anahtar. IDENTITY.")]
    public int Id { get; init; }

    [Description("Bagli oldugu belge. FK -> Document.Id")]
    public int DocumentId { get; init; }

    public int LineNo { get; set; }

    [Description("Stok karti referansi. FK -> Item.Id")]
    public int? ItemId { get; set; }

    public required string MaterialCode { get; set; }
    public required string MaterialName { get; set; }
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal LineTotal { get; set; }
    public string? CombinationCode { get; set; }
    public string? Notes { get; set; }

    [Description("Soft delete — listede gosterilir mi?")]
    public bool IsActive { get; set; } = true;
}
