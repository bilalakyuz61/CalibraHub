using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>
/// Items.TypeId icin sabit ID-tabanli tip rehberi. ID degerleri MaterialCardFieldCatalog.FieldOptions
/// (MaterialType field'inin SortOrder degerleri) ile tutarli — patlatma motoru bu sabit ID'lere gore
/// uretilebilir / sarf / hizmet ayrimini yapar.
/// </summary>
public enum ItemType
{
    [Description("Mamul")]
    FinishedGood = 1,

    [Description("Yari Mamul")]
    SemiFinished = 2,

    [Description("Hammadde")]
    RawMaterial = 3,

    [Description("Ambalaj")]
    Packaging = 4,

    [Description("Yardimci Malzeme")]
    Auxiliary = 5,

    [Description("Isletme Malzemesi")]
    Operating = 6,

    [Description("Hizmet")]
    Service = 7,

    [Description("Ticari Mal")]
    Merchandise = 8,

    [Description("Diger")]
    Other = 9,
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
        (int)ItemType.Packaging,
        (int)ItemType.Auxiliary,
        (int)ItemType.Operating,
        (int)ItemType.Merchandise,
    ];

    public static bool IsProducible(int? typeId) => typeId.HasValue && ProducibleTypeIds.Contains(typeId.Value);

    public static bool IsConsumable(int? typeId) => typeId.HasValue && ConsumableTypeIds.Contains(typeId.Value);
}
