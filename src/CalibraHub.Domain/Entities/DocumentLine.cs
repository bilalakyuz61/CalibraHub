using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Document tablosundaki belgelerin satir detaylari (stok, adet, birim fiyat, iskonto, satir toplami). DocumentId FK basliga baglidir; ItemId FK ise stok kartina. Malzeme kodu/adi Item tablosundan cozulur — DocumentLine tablosunda tutulmaz.")]
public sealed class DocumentLine
{
    [Description("Birincil anahtar. IDENTITY.")]
    public int Id { get; init; }

    [Description("Bagli oldugu belge. FK -> Document.Id")]
    public int DocumentId { get; init; }

    public int LineNo { get; set; }

    [Description("Stok karti referansi. FK -> Item.Id. Malzeme kodu/adi ve diger bilgiler buradan JOIN ile okunur.")]
    public int ItemId { get; set; }

    [Description("Birim referansi. FK -> Unit.Id. Birim kodu/adi buradan JOIN ile cozulur.")]
    public int? UnitId { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal LineTotal { get; set; }

    [Description("Secili kombinasyon. FK -> ProductConfiguration.Id (CONFIG kaydi). Kombinasyon kodu/adi buradan JOIN ile cozulur.")]
    public int? CombinationId { get; set; }

    [Description("Lokasyon referansi (depo/raf/goz). FK -> Location.Id. Malzeme kartindaki varsayilan lokasyondan turer — kullanici override edebilir.")]
    public int? LocationId { get; set; }

    public string? Notes { get; set; }

    [Description("Not panelinin satir acilislarinda otomatik acik gelmesi icin pinli mi?")]
    public bool NotesPinned { get; set; }

    [Description("Revize zinciri — bu satir hangi satirdan revize edildi? NULL ise orijinal satir. FK -> DocumentLine.Id (self-referencing). Zincir geriye dogru takip edilerek kac revize oldugu gorulebilir.")]
    public int? RevisedFromId { get; set; }

    [Description("Kalem bazli kaynak iz — bu sat�r hangi kaynak satirdan turetildi? Tekliften siparise donustururken her sipariş satirinin hangi teklif satirindan geldigini gosterir. NULL ise orijinal/manuel girilmis. FK -> DocumentLine.Id (self-referencing).")]
    public int? SourceLineId { get; set; }

    // ── Transient display fields (Item + Unit + ProductConfiguration + Location JOIN ile okunur; tabloya yazilmaz) ──
    public string? MaterialCode { get; set; }
    public string? MaterialName { get; set; }
    public string? UnitCode { get; set; }
    public string? UnitName { get; set; }
    public string? CombinationCode { get; set; }
    public string? LocationCode { get; set; }
    public string? LocationName { get; set; }
}
