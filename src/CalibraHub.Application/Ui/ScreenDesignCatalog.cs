using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Ui;

public static class ScreenDesignCatalog
{
    public const string MaterialCardsScreenCode = "items";
    public const string ProductConfigScreenCode = "product_configuration";
    public const string LocationsScreenCode = "locations";
    public const string UnitsScreenCode = "units";
    public const string ContactsScreenCode = "contacts";
    public const string DocumentApprovalScreenCode = "document_approval";
    public const string MaterialGroupsScreenCode = "material_groups";
    public const string DocumentsScreenCode = "documents";

    private static readonly ScreenDesignScreenDto[] Screens =
    [
        // Lojistik
        new(MaterialCardsScreenCode,          "Malzeme Kartlari",          "Lojistik", true),
        new(ProductConfigScreenCode,          "Urun Konfigurasyonu",       "Lojistik", true),
        new(LocationsScreenCode,     "Lokasyon Tanimlamalari",    "Lojistik", false),
        new(UnitsScreenCode, "Ölçü Birimleri", "Lojistik", false),
        new(MaterialGroupsScreenCode,         "Malzeme Gruplari",          "Lojistik", false),
        // Satis
        new(DocumentsScreenCode,            "Satis Teklifleri",          "Satis",    true),
        // Finans
        new(ContactsScreenCode,        "Cari Hesaplar",             "Finans",   true),
        // Onay Surecleri
        new(DocumentApprovalScreenCode,       "Belge Onay",                "Onay Surecleri", false),
    ];

    private static readonly IReadOnlyDictionary<string, ScreenDesignFieldDefinitionDto[]> StandardFieldDefinitions =
        new Dictionary<string, ScreenDesignFieldDefinitionDto[]>(StringComparer.OrdinalIgnoreCase)
        {
            [LocationsScreenCode] =
            [
                new("location_type_code", "Lokasyon Tipi", 1, true),
                new("parent_id", "Ust Kirilim", 1, false),
                new("location_code", "Lokasyon Kodu", 1, true),
                new("location_name", "Lokasyon Adi", 1, false),
                new("sort_order", "Siralama", 1, false),
                new("max_weight_capacity", "Maksimum Agirlik", 1, false),
                new("volume_capacity", "Hacim Kapasitesi", 1, false),
                new("is_active", "Aktif", 1, false)
            ],
            [UnitsScreenCode] =
            [
                new("unit_code", "Olcu Birimi Kodu", 1, true),
                new("unit_name", "Olcu Birimi Adi", 1, true),
                new("sort_order", "Siralama", 1, false),
                new("is_active", "Aktif", 1, false)
            ]
        };

    public static IReadOnlyCollection<ScreenDesignScreenDto> GetSupportedScreens() => Screens;

    public static bool IsSupportedScreen(string? screenCode) =>
        Screens.Any(x => string.Equals(x.ScreenCode, screenCode?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool UsesMaterialCardSchema(string? screenCode)
    {
        var normalized = NormalizeScreenCode(screenCode);
        var screen = Screens.FirstOrDefault(x => string.Equals(x.ScreenCode, normalized, StringComparison.OrdinalIgnoreCase));
        return screen?.UsesMaterialCardSchema == true;
    }

    public static string NormalizeScreenCode(string? screenCode)
    {
        var normalized = screenCode?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return MaterialCardsScreenCode;
        }

        return Screens.FirstOrDefault(x => string.Equals(x.ScreenCode, normalized, StringComparison.OrdinalIgnoreCase))?.ScreenCode
               ?? MaterialCardsScreenCode;
    }

    public static string GetScreenLabel(string? screenCode) =>
        Screens.FirstOrDefault(x => string.Equals(x.ScreenCode, NormalizeScreenCode(screenCode), StringComparison.OrdinalIgnoreCase))?.ScreenLabel
        ?? "Ekran";

    public static IReadOnlyCollection<ScreenDesignFieldDefinitionDto> GetFieldDefinitions(string screenCode)
    {
        var normalizedScreenCode = NormalizeScreenCode(screenCode);
        return StandardFieldDefinitions.TryGetValue(normalizedScreenCode, out var items)
            ? items
            : Array.Empty<ScreenDesignFieldDefinitionDto>();
    }

    public static ScreenDesignLayoutDto GetDefaultLayout(string screenCode)
    {
        var normalizedScreenCode = NormalizeScreenCode(screenCode);
        var screenLabel = GetScreenLabel(normalizedScreenCode);
        var fields = GetFieldDefinitions(normalizedScreenCode);

        if (fields.Count == 0)
        {
            return new ScreenDesignLayoutDto(
                normalizedScreenCode,
                screenLabel,
                Array.Empty<ScreenDesignTabDto>(),
                Array.Empty<ScreenDesignItemDto>(),
                Array.Empty<ScreenDesignFieldDefinitionDto>());
        }

        var tabs = normalizedScreenCode switch
        {
            LocationsScreenCode =>
            [
                new ScreenDesignTabDto("general", "Genel Bilgiler", 10, true),
                new ScreenDesignTabDto("capacity", "Kapasite", 20, true)
            ],
            UnitsScreenCode =>
            [
                new ScreenDesignTabDto("general", "Genel Bilgiler", 10, true)
            ],
            _ => Array.Empty<ScreenDesignTabDto>()
        };

        var items = normalizedScreenCode switch
        {
            LocationsScreenCode =>
            [
                new ScreenDesignItemDto("location_type_code", "Lokasyon Tipi", "general", 10, 1, true, true),
                new ScreenDesignItemDto("parent_id", "Ust Kirilim", "general", 20, 1, true, false),
                new ScreenDesignItemDto("location_code", "Lokasyon Kodu", "general", 30, 1, true, true),
                new ScreenDesignItemDto("location_name", "Lokasyon Adi", "general", 40, 1, true, false),
                new ScreenDesignItemDto("sort_order", "Siralama", "general", 50, 1, true, false),
                new ScreenDesignItemDto("is_active", "Aktif", "general", 60, 1, true, false),
                new ScreenDesignItemDto("max_weight_capacity", "Maksimum Agirlik", "capacity", 10, 1, true, false),
                new ScreenDesignItemDto("volume_capacity", "Hacim Kapasitesi", "capacity", 20, 1, true, false)
            ],
            UnitsScreenCode =>
            [
                new ScreenDesignItemDto("unit_code", "Olcu Birimi Kodu", "general", 10, 1, true, true),
                new ScreenDesignItemDto("unit_name", "Olcu Birimi Adi", "general", 20, 1, true, true),
                new ScreenDesignItemDto("sort_order", "Siralama", "general", 30, 1, true, false),
                new ScreenDesignItemDto("is_active", "Aktif", "general", 40, 1, true, false)
            ],
            _ => Array.Empty<ScreenDesignItemDto>()
        };

        return new ScreenDesignLayoutDto(
            normalizedScreenCode,
            screenLabel,
            tabs,
            items,
            fields);
    }
}
