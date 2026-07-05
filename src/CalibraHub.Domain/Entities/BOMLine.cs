using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

[Description("Urun agaci bilesenleri. BOMId FK -> BOM.Id (baslik). ItemId FK -> Items.id (bilesen malzemesi). ConfigId FK -> ItemConfiguration.Id (varyant). Quantity = 1 birim parent icin gerekli bilesen adedi; ScrapRatio = fire orani.")]
public sealed class BOMLine
{
    public int Id { get; init; }
    public int BOMId { get; init; }
    public int ItemId { get; init; }
    public int? ConfigId { get; init; }

    // Quantity ve ScrapRatio mutable — ChangeQuantity/ChangeScrap domain davranisi
    // entity'yi yerinde guncelliyor. SaveBOMAsync icin baslangic deger property
    // initializer'i ile set ediliyor; sonradan domain metotlariyla degisebilir.
    public decimal Quantity { get; set; } = 1;
    public decimal ScrapRatio { get; set; } = 0;

    public Guid LineGuid { get; init; }

    /// <summary>
    /// Satır açıklaması (opsiyonel, max 1000). Kullanıcının bileşene not düşmesi için —
    /// örn. "montajda yapıştırıcı ile", "tedarikçi X'ten". 2026-07-05: UI'da toplanan
    /// değer daha önce hiçbir katmanda taşınmıyordu; uçtan uca eklendi.
    /// </summary>
    public string? Note { get; init; }

    // Audit dortlusu — header (BOM) ile birlikte bu satir da kim/ne zaman bilgisi
    // tasir. UpdateBOMAsync mevcut tum line'lari DELETE+INSERT yaptigi icin
    // satir-bazli Updated kolonu olmaz (her save sonrasi yeni satir = yeni Created).
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;

    // ═══════════════════════════════════════════════════════════════════
    // Domain davranis metotlari (rapor 2026-05-17 madde 3.2 — rich domain).
    // Setter'lar GERIYE UYUM icin acik birakildi; service'ler dogrudan set
    // edebiliyor. Yeni kod bu metotlari cagirip invariant garantilerini alir.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Satir invariant'lari — bir bilesenin tutarli olmasi icin gereken minimum kurallar.
    /// SaveBOMAsync save oncesi her satira cagirir; ihlal -> DomainException.
    /// Mesajlar kullanici dostu — teknik ID ifsa edilmez (rapor 2026-05-17 madde 3.11).
    /// </summary>
    public void EnsureValid()
    {
        DomainException.ThrowIf(ItemId <= 0,
            "Her bilesen icin bir malzeme secmek zorunludur.");
        DomainException.ThrowIf(Quantity <= 0,
            "Bilesen miktari sifirdan buyuk olmalidir.");
        DomainException.ThrowIf(ScrapRatio < 0,
            "Fire orani negatif olamaz (0 veya daha buyuk olmalidir).");
    }

    /// <summary>
    /// Miktari guvenli sekilde guncelle — invariant'tan gecirir.
    /// </summary>
    public void ChangeQuantity(decimal newQuantity)
    {
        DomainException.ThrowIf(newQuantity <= 0,
            "Bilesen miktari sifirdan buyuk olmalidir.");
        Quantity = newQuantity;
    }

    /// <summary>
    /// Fire oranini guvenli sekilde guncelle — invariant'tan gecirir.
    /// </summary>
    public void ChangeScrap(decimal newScrap)
    {
        DomainException.ThrowIf(newScrap < 0,
            "Fire orani negatif olamaz (0 veya daha buyuk olmalidir).");
        ScrapRatio = newScrap;
    }

    /// <summary>
    /// Yeni satir uretici — invariant'tan gecmis bir BOMLine doner. Service
    /// resolved tuple'larini bu factory ile entity'ye cevirir.
    /// </summary>
    public static BOMLine Create(int itemId, int? configId, decimal quantity, decimal scrapRatio, int? createdById = null, string? note = null)
    {
        var line = new BOMLine
        {
            ItemId      = itemId,
            ConfigId    = configId,
            Quantity    = quantity,
            ScrapRatio  = scrapRatio,
            LineGuid    = Guid.NewGuid(),
            CreatedById = createdById,
            Note        = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
        line.EnsureValid();
        return line;
    }
}
