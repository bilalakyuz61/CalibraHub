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

    /// <summary>Fiyat modu — <see cref="KitPriceMode"/>. 4 mod (2026-08-03 genisleme):
    /// FixedPackage = kit'in tek elle girilen sabit fiyati (FixedPrice, eski "Fixed");
    /// FixedComponent = bilesenlerin kit tanimindaki elle fiyatlari x miktar toplami (ItemKitLine.UnitPrice);
    /// ListPackage = kit kartinin KENDI fiyat listesi (Genel Liste) fiyati, satis aninda cozulur;
    /// ListComponent = bilesenlerin fiyat listesi fiyatlari x miktar toplami, satis aninda (eski "RollUp").</summary>
    public string PriceMode { get; init; } = KitPriceMode.FixedPackage;

    /// <summary>FixedPackage modda kit'in varsayilan birim satis fiyati. Belgeye eklenince
    /// kalem satirina otomatik gelir. Diger 3 modda NULL (sistem/tanim hesaplar).</summary>
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
        DomainException.ThrowIf(!KitPriceMode.All.Contains(PriceMode),
            "Fiyat modu yalniz Sabit Paket, Sabit Bilesen, Liste Paket veya Liste Bilesen olabilir.");
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

/// <summary>
/// Kit fiyatlandirma modlari. DB'de ItemKit.PriceMode NVARCHAR(20) olarak tutulur.
/// 2026-08-03 genisleme (PageComment Seq 1078): 2 moddan 4 moda cikildi. Eski DB degerleri
/// ('Fixed'/'RollUp') CalibraDatabaseInitializer.EnsureItemKitTablesAsync icindeki idempotent
/// UPDATE ile yeni degerlere (FixedPackage/ListComponent) migrate edilir — kodda alias/parse
/// katmani YOK, DB tek dogru kaynaktir.
/// </summary>
public static class KitPriceMode
{
    /// <summary>Kit kendi sabit birim satis fiyati (ItemKit.FixedPrice) ile satilir. Sunucu bu
    /// degeri hic ezmez — belgeye eklenirken client'in tasidigi deger aynen kullanilir. Eski adi "Fixed".</summary>
    public const string FixedPackage = "FixedPackage";

    /// <summary>Her bilesenin kit tanimindaki ELLE girilmis birim fiyati (ItemKitLine.UnitPrice)
    /// x miktar toplami. Fiyat listesi cagrisi YOK — tamamen kit tanimindan gelir. YENI (2026-08-03).</summary>
    public const string FixedComponent = "FixedComponent";

    /// <summary>Kit kartinin KENDI fiyat listesi (Genel Liste/cari) fiyati — satis aninda
    /// IPriceListService.ResolveLinePricesAsync ile kit ItemId'si uzerinden cozulur. YENI (2026-08-03).</summary>
    public const string ListPackage = "ListPackage";

    /// <summary>Kit fiyati bilesenlerin fiyat listesi (Genel Liste/cari) fiyatlari x miktar
    /// toplamindan, satis aninda hesaplanir. Eski adi "RollUp".</summary>
    public const string ListComponent = "ListComponent";

    /// <summary>Whitelist — SaveKitAsync normalize + ItemKit.EnsureValid burada tek kaynaktan okur.</summary>
    public static readonly IReadOnlyCollection<string> All = new[]
        { FixedPackage, FixedComponent, ListPackage, ListComponent };
}
