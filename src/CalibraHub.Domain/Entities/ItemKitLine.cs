using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

[Description("Kit bilesenleri. ItemKitId FK -> ItemKit.Id (baslik). ItemId FK -> Items.id (bilesen malzemesi). ConfigId FK -> ItemConfiguration.Id (varyant). Quantity = 1 kit icin gereken bilesen adedi. Bilesen seri/lot takipli veya yapilandirmali (kombinasyon) olabilir — kit'in kendisi degildir. UpdateKitAsync DELETE+INSERT yaptigi icin satir-bazli Updated kolonu olmaz.")]
public sealed class ItemKitLine
{
    public int Id { get; init; }
    public int ItemKitId { get; init; }

    /// <summary>Bilesen malzeme karti (Items.id).</summary>
    public int ItemId { get; init; }

    /// <summary>Bilesenin varyanti (ItemConfiguration.Id) — kombinasyon-takipli bilesen icin.</summary>
    public int? ConfigId { get; init; }

    // Quantity mutable — ChangeQuantity domain davranisi yerinde gunceller.
    public decimal Quantity { get; set; } = 1;

    /// <summary>Bilesenin kit tanimindaki ELLE girilmis birim fiyati — yalniz kit
    /// PriceMode=FixedComponent iken anlamlidir (KitPriceMode.FixedComponent). Diger
    /// modlarda NULL tutulur (SaveKitAsync normalize eder). Fiyat listesi degil, dogrudan
    /// bu deger kitUnitPrice = Sum(UnitPrice * Quantity) formulunde kullanilir.</summary>
    public decimal? UnitPrice { get; init; }

    /// <summary>Bilesenin kit tanimindaki secili olcu birimi (Unit.Id) — NULL ise bilesen
    /// malzemesinin kendi varsayilan birimi kabul edilir. Miktar/baz-birim cevrimi bu
    /// asamada YAPILMAZ (yalniz secim persist edilir; DocumentService kit patlatmasi
    /// henuz bu alani kullanmiyor).</summary>
    public int? UnitId { get; init; }

    public Guid LineGuid { get; init; }

    /// <summary>Satir aciklamasi (opsiyonel, max 1000) — kullanicinin bilesene not dusmesi icin.</summary>
    public string? Note { get; init; }

    // Audit — header ile birlikte satir da kim/ne zaman bilgisi tasir.
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Satir invariant'lari. Mesajlar kullanici dostu — teknik ID ifsa edilmez.
    /// </summary>
    public void EnsureValid()
    {
        DomainException.ThrowIf(ItemId <= 0,
            "Her bilesen icin bir malzeme secmek zorunludur.");
        DomainException.ThrowIf(Quantity <= 0,
            "Bilesen miktari sifirdan buyuk olmalidir.");
        DomainException.ThrowIf(UnitPrice.HasValue && UnitPrice.Value < 0,
            "Bilesen birim fiyati negatif olamaz.");
    }

    /// <summary>Miktari guvenli sekilde guncelle — invariant'tan gecirir.</summary>
    public void ChangeQuantity(decimal newQuantity)
    {
        DomainException.ThrowIf(newQuantity <= 0,
            "Bilesen miktari sifirdan buyuk olmalidir.");
        Quantity = newQuantity;
    }

    /// <summary>Yeni satir uretici — invariant'tan gecmis bir ItemKitLine doner.
    /// <paramref name="unitPrice"/> yalniz kit PriceMode=FixedComponent iken doldurulur
    /// (SaveKitAsync diger modlarda null gecirir).</summary>
    public static ItemKitLine Create(int itemId, int? configId, decimal quantity, int? createdById = null,
        string? note = null, decimal? unitPrice = null, int? unitId = null)
    {
        var line = new ItemKitLine
        {
            ItemId      = itemId,
            ConfigId    = configId,
            Quantity    = quantity,
            UnitPrice   = unitPrice,
            UnitId      = unitId,
            LineGuid    = Guid.NewGuid(),
            CreatedById = createdById,
            Note        = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
        line.EnsureValid();
        return line;
    }
}
