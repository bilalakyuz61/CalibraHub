using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>
/// Items.TypeId icin sabit ID-tabanli tip rehberi.
///
/// KAYNAK-I HAKIKAT: MaterialCardEdit.cshtml'deki "Malzeme Tipi" acilir listesi
/// (statik &lt;option value="..."&gt;) ve ItemImportHandler.MaterialTypes. Items.TypeId
/// degerlerini fiilen YAZAN iki yol bunlardir; enum onlari yansitir, tersi degil.
///
/// UYARI (2026-08-31): Bu enum eskiden 1=Mamul / 3=Hammadde diye numaralanmisti —
/// yani UI'in yazdiginin TAM TERSI. ItemTypeCatalog.IsProducible bu yuzden mamulu
/// "uretilemez", hammaddeyi "uretilebilir" sayiyordu ve MRP sessizce yanlis
/// calisiyordu. Numaralar burada degistirilirken MUTLAKA yukaridaki iki yazma
/// yolu ile birebir karsilastir.
/// </summary>
public enum ItemType
{
    [Description("Hammadde")]
    RawMaterial = 1,

    [Description("Yari Mamul")]
    SemiFinished = 2,

    [Description("Mamul")]
    FinishedGood = 3,

    [Description("Ticari Mal")]
    Merchandise = 4,

    [Description("Sarf Malzemesi")]
    Consumable = 5,

    /// <summary>
    /// Kit / paket urun (phantom bundle). Fiziksel stok degil, birden fazla stogu
    /// tek kod altinda toplayan mantiksal gruplama. Satis/teklif/sipariste tek kalem,
    /// irsaliyede bilesenlerine patlar. Stok/seri/lot etkisi bilesen seviyesindedir.
    /// Icerik ItemKit + ItemKitLine tablolarinda tutulur.
    /// </summary>
    [Description("Kit")]
    Kit = 10,
}

public static class ItemTypeCatalog
{
    /// <summary>Uretilebilir tipler — is emri / patlatma sirasinda alt is emri (semi) veya
    /// kendi is emri (finished) olarak ele alinir.</summary>
    public static readonly IReadOnlyList<int> ProducibleTypeIds =
        [(int)ItemType.FinishedGood, (int)ItemType.SemiFinished];

    /// <summary>Sarf/satin alma tipleri — BOM bilesenleri olarak malzeme listesine girer.</summary>
    public static readonly IReadOnlyList<int> ConsumableTypeIds =
    [
        (int)ItemType.RawMaterial,
        (int)ItemType.Consumable,
        (int)ItemType.Merchandise,
    ];

    public static bool IsProducible(int? typeId) => typeId.HasValue && ProducibleTypeIds.Contains(typeId.Value);

    public static bool IsConsumable(int? typeId) => typeId.HasValue && ConsumableTypeIds.Contains(typeId.Value);

    public static bool IsKit(int? typeId) => typeId == (int)ItemType.Kit;
}
