namespace CalibraHub.Application.Contracts;

public sealed record MaterialCardFieldDefinition(
    string Key,
    string Label,
    int DisplayOrder,
    bool DefaultVisible = true,
    bool DefaultRequired = false,
    string DataType = "STRING");

public static class MaterialCardFieldCatalog
{
    public const string UnitName = "unit_name";
    public const string MaterialType = "material_type";

    public const string PurchasePrice = "purchase_price";

    public static readonly IReadOnlyCollection<MaterialCardFieldDefinition> Definitions =
    [
        new(UnitName, "Olcu Birimi", 10),
        new(MaterialType, "Malzeme Tipi", 5, DataType: "DROPDOWN"),
        new(PurchasePrice, "Alis Fiyati", 15, DataType: "DECIMAL"),
        new("width", "En", 20, DataType: "DECIMAL"),
        new("height", "Boy", 21, DataType: "DECIMAL"),
        new("length", "Genislik", 22, DataType: "DECIMAL"),
        new("weight", "Agirlik", 23, DataType: "DECIMAL"),
        new("volume", "Hacim", 24, DataType: "DECIMAL"),
        new("default_supplier", "Varsayilan Tedarikci", 25),
        new("tax_rate", "KDV Orani", 6, DataType: "DECIMAL"),
        new("unit_conv_1_code", "Birim 1 Kod", 30),
        new("unit_conv_1_mult", "Birim 1 Carpan", 31, DataType: "DECIMAL"),
        new("unit_conv_2_code", "Birim 2 Kod", 32),
        new("unit_conv_2_mult", "Birim 2 Carpan", 33, DataType: "DECIMAL"),
        new("unit_conv_3_code", "Birim 3 Kod", 34),
        new("unit_conv_3_mult", "Birim 3 Carpan", 35, DataType: "DECIMAL"),
        new("unit_conv_4_code", "Birim 4 Kod", 36),
        new("unit_conv_4_mult", "Birim 4 Carpan", 37, DataType: "DECIMAL"),
        new("unit_conv_5_code", "Birim 5 Kod", 38),
        new("unit_conv_5_mult", "Birim 5 Carpan", 39, DataType: "DECIMAL"),
    ];

    public static readonly IReadOnlyCollection<(string FieldKey, string OptionKey, string OptionLabel, int SortOrder)> FieldOptions =
    [
        (MaterialType, "mamul",       "Mamul",           1),
        (MaterialType, "yari_mamul",  "Yari Mamul",      2),
        (MaterialType, "hammadde",    "Hammadde",        3),
        (MaterialType, "ambalaj",     "Ambalaj",         4),
        (MaterialType, "yardimci",    "Yardimci Malzeme", 5),
        (MaterialType, "isletme",     "Isletme Malzemesi",6),
        (MaterialType, "hizmet",      "Hizmet",          7),
        (MaterialType, "ticari_mal",  "Ticari Mal",      8),
        (MaterialType, "diger",       "Diger",           9),
        (MaterialType, "kit",         "Kit",             10),
    ];

    public static bool IsSupported(string key) =>
        Definitions.Any(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
}
