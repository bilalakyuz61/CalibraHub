using System.ComponentModel;
using CalibraHub.Domain.Common;

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

    [Description("Miktarın ana birime (Item.UnitId) çevrilmiş hali. Yazımda Quantity * ItemUnits.Multiplier ile hesaplanır (UnitId ana birim/NULL ise = Quantity). TÜM stok bakiye/hareket hesapları bunu kullanır — farklı birimlerden hareketler tutarlı toplanır. UnitId = girilen birim (gösterim).")]
    public decimal BaseQuantity { get; set; }

    [Description("Satış siparişi rezervasyonu (Faz 2): teslim edilen ana-birim miktar. Açık/rezerve miktar = BaseQuantity - DeliveredQuantity. Teslimat aksiyonuyla artar.")]
    public decimal DeliveredQuantity { get; set; }

    public decimal UnitPrice { get; set; }
    public decimal DiscountRate { get; set; }
    public decimal LineTotal { get; set; }

    [Description("Secili kombinasyon. FK -> ProductConfiguration.Id (CONFIG kaydi). Kombinasyon kodu/adi buradan JOIN ile cozulur.")]
    public int? CombinationId { get; set; }

    [Description("Lokasyon referansi (depo/raf/goz). FK -> Location.Id. Malzeme kartindaki varsayilan lokasyondan turer — kullanici override edebilir.")]
    public int? LocationId { get; set; }

    [Description("Kalem bazlı teslim tarihi (sipariş satırı için). NULL ise üst belge Document.DeliveryDate geçerli olur (fallback).")]
    public DateTime? DeliveryDate { get; set; }

    [Description("Kalem bazlı teslim süresi (gün). UI'da DeliveryDate ile çift yönlü senkron. NULL ise üst belge Document.DeliveryDays geçerli olur.")]
    public int? DeliveryDays { get; set; }

    public string? Notes { get; set; }

    [Description("Not panelinin satir acilislarinda otomatik acik gelmesi icin pinli mi?")]
    public bool NotesPinned { get; set; }

    [Description("Revize zinciri — bu satir hangi satirdan revize edildi? NULL ise orijinal satir. FK -> DocumentLine.Id (self-referencing). Zincir geriye dogru takip edilerek kac revize oldugu gorulebilir.")]
    public int? RevisedFromId { get; set; }

    [Description("Kalem bazli kaynak iz — bu sat�r hangi kaynak satirdan turetildi? Tekliften siparise donustururken her sipariş satirinin hangi teklif satirindan geldigini gosterir. NULL ise orijinal/manuel girilmis. FK -> DocumentLine.Id (self-referencing).")]
    public int? SourceLineId { get; set; }

    // ── İhtiyaç Kaydı karşılama takip alanları (alis_talebi satırları için) ──
    // Diğer belge tiplerinde 0 kalır. DB'de FulfilledFromStock/ByPurchase/FulfillmentStatus kolonları.
    /// <summary>Stoktan depo transferi ile karşılanan miktar (alis_talebi). Diğer belge tiplerinde 0.</summary>
    public decimal FulfilledFromStock { get; set; }
    /// <summary>Satın alma belgeleri (teklif/sipariş) ile karşılanan miktar. Diğer belge tiplerinde 0.</summary>
    public decimal FulfilledByPurchase { get; set; }
    /// <summary>Karşılama durumu: 0=Pending, 1=Partial, 2=Full, 3=Cancelled.</summary>
    public int FulfillmentStatus { get; set; }

    // ── Stok hareketi alanları (2026-07-02 konsolidasyonu) ──
    // Bu satır fiilen stok hareketi yaratıyorsa dolar; ticari satırlarda NULL kalır.
    [Description("Stok hareket yönü — StockMovementType enum (Issue=1/Receipt=2/Transfer=3/Adjust=4). NULL = ticari/stok-etkilemez satır.")]
    public byte? MovementType { get; set; }

    [Description("Transfer/sarf kaynağı lokasyon. FK -> Location.Id. Mevcut LocationId hedef/tekil lokasyon olarak kullanılır.")]
    public int? FromLocationId { get; set; }

    [Description("Stok hareketinin birim maliyet snapshot'ı (fiyat listesinden veya manuel).")]
    public decimal? UnitCost { get; set; }

    [Description("Parti/lot takibi.")]
    public string? LotNo { get; set; }

    public string? FromLocationCode { get; set; }
    public string? FromLocationName { get; set; }

    // ── Transient display fields (Item + Unit + ProductConfiguration + Location JOIN ile okunur; tabloya yazilmaz) ──
    public string? MaterialCode { get; set; }
    public string? MaterialName { get; set; }
    public string? UnitCode { get; set; }
    public string? UnitName { get; set; }
    public string? CombinationCode { get; set; }
    public string? LocationCode { get; set; }
    public string? LocationName { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    // Domain davranis metotlari (rapor §2.2 — rich domain, additive)
    //
    // Mevcut setter'lar GERIYE UYUM icin acik birakildi; eski service'ler
    // (DocumentService, DocumentImportService) dogrudan set ediyor.
    // Yeni kod bu metotlari cagirip invariant garantilerini alir.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Satir toplamini (LineTotal) hesaplar: Quantity * UnitPrice * (1 - DiscountRate/100).
    /// Invariant'lar: Quantity > 0, UnitPrice >= 0, DiscountRate 0-100. Yuvarlama 2 ondalik.
    /// </summary>
    public void CalculateLineTotal()
    {
        DomainException.ThrowIf(Quantity <= 0,
            $"Quantity pozitif olmali (su an: {Quantity})");
        DomainException.ThrowIf(UnitPrice < 0,
            $"UnitPrice negatif olamaz (su an: {UnitPrice})");
        DomainException.ThrowIf(DiscountRate < 0 || DiscountRate > 100,
            $"DiscountRate 0-100 araliginda olmali (su an: {DiscountRate})");

        var gross = Quantity * UnitPrice;
        var discountAmount = gross * (DiscountRate / 100m);
        LineTotal = Math.Round(gross - discountAmount, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Miktar degisikligi + otomatik recalculate. ChangeQuantity(0) yasak — line silmek
    /// icin Document.RemoveLine kullanin.
    /// </summary>
    public void ChangeQuantity(decimal newQuantity)
    {
        DomainException.ThrowIf(newQuantity <= 0,
            $"Quantity sifir veya negatif olamaz. Satiri silmek icin Document.RemoveLine kullanin.");
        Quantity = newQuantity;
        CalculateLineTotal();
    }

    /// <summary>
    /// Iskonto orani degisikligi + otomatik recalculate.
    /// </summary>
    public void ApplyDiscount(decimal discountRate)
    {
        DomainException.ThrowIf(discountRate < 0 || discountRate > 100,
            $"DiscountRate 0-100 araliginda olmali (verilen: {discountRate})");
        DiscountRate = discountRate;
        CalculateLineTotal();
    }

    /// <summary>
    /// Birim fiyat degisikligi + otomatik recalculate.
    /// </summary>
    public void ChangeUnitPrice(decimal newPrice)
    {
        DomainException.ThrowIf(newPrice < 0,
            $"UnitPrice negatif olamaz (verilen: {newPrice})");
        UnitPrice = newPrice;
        CalculateLineTotal();
    }
}
