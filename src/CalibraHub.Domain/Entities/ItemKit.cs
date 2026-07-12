using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

[Description("Kit (paket urun) baslik. ItemId FK -> Items.id (TypeId=10 Kit tipindeki karti). Bilesenler ItemKitLine tablosunda 1-N. Kit fiziksel stok DEGIL, birden fazla stogu tek kod altinda toplayan mantiksal gruplama (phantom bundle) — stok/seri/lot etkisi bilesen seviyesindedir. PriceMode: Fixed (kit kendi sabit fiyati, FixedPrice) / RollUp (bilesen fiyatlari toplami, runtime hesap). VersionNo her revizyonda artar; belge kit'i kullandiginda o anki icerik belge satirina snapshot'lanir, sonraki revizyon gecmis belgeleri etkilemez. IsActive=0 soft-delete (gecmis belgeler orphan kalmasin).")]
public class ItemKit
{
    public int Id { get; init; }

    /// <summary>Kit'in kendisi olan malzeme karti (Items.id, TypeId=10 Kit).</summary>
    public int ItemId { get; init; }

    /// <summary>Revizyon numarasi — her save'de artar. Belge snapshot'i bu degeri saklar
    /// ("bu belge kit v3'u kullandi"). Kit v4'e cikinca eski belge hala v3 icerigini tasir.</summary>
    public int VersionNo { get; init; } = 1;

    /// <summary>Fiyat modu — <see cref="KitPriceMode"/>. Fixed = kit kendi fiyati (FixedPrice);
    /// RollUp = bilesen satis fiyatlari x miktar toplami (runtime hesaplanir).</summary>
    public string PriceMode { get; init; } = KitPriceMode.Fixed;

    /// <summary>Fixed modda kit'in varsayilan birim satis fiyati. Belgeye eklenince
    /// kalem satirina otomatik gelir. RollUp modda NULL (sistem hesaplar).</summary>
    public decimal? FixedPrice { get; init; }

    public string? Description { get; init; }

    // Standart kolon seti (CLAUDE.md) — soft-delete + audit dortlusu.
    public bool IsActive { get; init; } = true;
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }

    // Lines: init koleksiyon, icindeki List domain metotlariyla mutate edilir.
    public ICollection<ItemKitLine> Lines { get; init; } = new List<ItemKitLine>();

    // ═══════════════════════════════════════════════════════════════════
    // Domain davranis metotlari (BOM deseninin yalin klonu — rota/fire yok).
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Satir ekler. Duplicate (ayni ItemId+ConfigId) korumasi + line invariant'i.
    /// Kullanici dostu mesajlar — teknik ID ifsa edilmez.
    /// </summary>
    public void AddLine(ItemKitLine line)
    {
        DomainException.ThrowIf(line is null, "Bilesen bilgisi eksik.");
        line!.EnsureValid();
        DomainException.ThrowIf(
            Lines.Any(l => l.ItemId == line.ItemId && (l.ConfigId ?? 0) == (line.ConfigId ?? 0)),
            "Ayni bilesen birden fazla kez eklenemez. Lutfen kit icerigini gozden geciriniz.");
        Lines.Add(line);
    }

    /// <summary>
    /// Tum kit seviyesi invariant'lari — save oncesi son kontrol noktasi.
    /// </summary>
    public void EnsureValid()
    {
        DomainException.ThrowIf(ItemId <= 0,
            "Kit malzeme karti secilmelidir.");
        DomainException.ThrowIf(Lines.Count == 0,
            "Kit icerisinde en az bir bilesen olmalidir.");
        DomainException.ThrowIf(
            PriceMode != KitPriceMode.Fixed && PriceMode != KitPriceMode.RollUp,
            "Fiyat modu yalniz 'Fixed' (sabit) veya 'RollUp' (bilesen toplami) olabilir.");
        DomainException.ThrowIf(FixedPrice.HasValue && FixedPrice.Value < 0,
            "Kit fiyati negatif olamaz.");

        // Defansif duplicate tarama (service entity'yi koleksiyon initializer ile
        // dogrudan kursaydi AddLine cagrilmazdi — bu tarama o senaryoyu da kapsar).
        var hasDup = Lines.GroupBy(l => (l.ItemId, l.ConfigId ?? 0)).Any(g => g.Count() > 1);
        DomainException.ThrowIf(hasDup,
            "Kit icinde ayni bilesen birden fazla kez var. Lutfen icerigi gozden geciriniz.");

        foreach (var line in Lines)
            line.EnsureValid();
    }
}

/// <summary>Kit fiyatlandirma modlari. DB'de ItemKit.PriceMode NVARCHAR(20) olarak tutulur.</summary>
public static class KitPriceMode
{
    /// <summary>Kit kendi sabit birim satis fiyati (ItemKit.FixedPrice) ile satilir.</summary>
    public const string Fixed = "Fixed";

    /// <summary>Kit fiyati bilesenlerin satis fiyatlari x miktar toplamindan hesaplanir.</summary>
    public const string RollUp = "RollUp";
}
