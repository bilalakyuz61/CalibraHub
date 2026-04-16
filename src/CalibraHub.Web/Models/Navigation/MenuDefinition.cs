namespace CalibraHub.Web.Models.Navigation;

/// <summary>
/// React Shell (glassmorphism kabuk) icin ana navigasyon menusunun
/// static tanimi. <c>_MainMenu.cshtml</c> icerigini bire bir yansitir —
/// ayni 8 grup, ayni alt ogeler, ayni rotalar.
///
/// Kullanim (Views/Shared/_Layout.cshtml icinde):
///     var menu = MenuDefinition.GetMainMenu(isSystemAdmin);
///     inline JSON olarak window.__CALIBRA_SHELL_CONFIG__.menu'ye yerlestirilir
///
/// Yeni bir menu ogesi eklemek icin: uygun grubun children listesine
/// yeni bir MenuNode ekle. Icon'lar Lucide React isimleridir (Package, Boxes,
/// Folder, vb. — React tarafinda <c>Icons[name]</c> ile cozulur).
/// </summary>
public static class MenuDefinition
{
    /// <summary>Tek bir menu dugumu (grup veya link).</summary>
    public sealed record MenuNode(
        string Key,                          // Unique — "logistics.materials"
        string Label,                        // Kullaniciya gosterilen metin
        string? Icon,                        // Lucide icon adi (null ise CircleDot fallback)
        string? Url,                         // Tiklaninca acilacak URL — null ise sadece grup basligi
        IReadOnlyList<MenuNode>? Children);  // Nested alt ogeler

    /// <summary>
    /// Tam menu agacini doner.
    /// </summary>
    /// <param name="isSystemAdmin">
    /// admin@calibra.local ise Setup/Definitions ogesi gosterilir
    /// (mevcut _MainMenu.cshtml'deki conditional aynen korunur).
    /// </param>
    public static IReadOnlyList<MenuNode> GetMainMenu(bool isSystemAdmin)
    {
        var settingsChildren = new List<MenuNode>
        {
            new("settings.company",           "Şirket Ayarları",        "Building2",   "/Admin/CompanySettings",    null),
            new("settings.integrationevents", "Entegrasyon Tanimlari",  "Zap",         "/Admin/IntegrationEvents",  null),
            new("settings.viewsettings",      "Ekran Tasarim Ayarlari", "LayoutGrid",  "/Admin/ViewSettings",       null),
        };
        if (isSystemAdmin)
        {
            settingsChildren.Add(
                new("settings.setupdefinitions", "Sirket ve Kullanici Tanimlari", "UserCog", "/Setup/Definitions", null));
        }

        return new List<MenuNode>
        {
            // ────────────── 1. Genel ──────────────
            new("general", "Genel", "LayoutList", null, new List<MenuNode>
            {
                new("general.notes", "Notlar", "FileText", "/Notes", null),
                new("general.orgchart", "Organizasyon Semasi", "Network", "/OrgChart", null),
            }),

            // ────────────── 2. Onay Islemleri ──────────────
            new("approval", "Onay Islemleri", "CheckCircle2", null, new List<MenuNode>
            {
                new("approval.documents", "Elektronik Belgeler", "Files", null, new List<MenuNode>
                {
                    new("approval.einvoice",  "e-Fatura",   "FileText", "/Approval/EInvoice",  null),
                    new("approval.earchive",  "e-Arsiv",    "Archive",  "/Approval/EArchive",  null),
                    new("approval.edispatch", "e-Irsaliye", "Truck",    "/Approval/EDispatch", null),
                }),
            }),

            // ────────────── 3. Lojistik ──────────────
            new("logistics", "Lojistik", "Package", null, new List<MenuNode>
            {
                new("logistics.fixed", "Sabit Tanimlamalar", "Folder", null, new List<MenuNode>
                {
                    new("logistics.materials",     "Malzeme Kartlari",      "Boxes",   "/Logistics/MaterialCards",        null),
                    new("logistics.configuration", "Urun Ozellik Listesi",  "Sliders", "/Logistics/ProductConfiguration", null),
                }),
                new("logistics.sales", "Satis", "TrendingUp", null, new List<MenuNode>
                {
                    new("logistics.salesquotes", "Satis Teklifi", "FileText", "/Sales/SalesQuotes", null),
                }),
            }),

            // ────────────── 4. Uretim ──────────────
            new("production", "Uretim", "Factory", null, new List<MenuNode>
            {
                new("production.tree", "Urun Agaci", "Network", "/Logistics/ProductTrees", null),
            }),

            // ────────────── 5. Finans ──────────────
            new("finance", "Finans", "Coins", null, new List<MenuNode>
            {
                new("finance.definitions", "Tanimlamalar", "Folder", null, new List<MenuNode>
                {
                    new("finance.accounts", "Cari Hesaplar", "Users", "/Finance/ContactAccounts", null),
                }),
            }),

            // ────────────── 6. Genel Tanimlamalar ──────────────
            new("generaldefs", "Genel Tanimlamalar", "Settings2", null, new List<MenuNode>
            {
                new("gendef.salesreps",   "Satis Temsilcileri",         "UserCircle",  "/GeneralDefinitions/SalesRepresentatives", null),
                new("gendef.currencies",  "Doviz Tanimlamalari",        "DollarSign",  "/GeneralDefinitions/Currencies",           null),
                new("gendef.warehouses",  "Lokasyon Tanimlamalari",     "MapPin",      "/Logistics/WarehouseLocations",            null),
                new("gendef.measureunit", "Olcu Birimi Tanimlamalari",  "Ruler",       "/Logistics/MeasureUnitDefinitions",        null),
                new("gendef.cardgroups",  "Grup Tanimlamalari",         "Layers",      "/Definitions/CardGroups",                  null),
                new("gendef.pricelist",   "Fiyat Listesi",              "Tag",         "/PriceList/PriceGroups",                   null),
            }),

            // ────────────── 7. Tasarim ──────────────
            new("design", "Tasarim", "LayoutGrid", null, new List<MenuNode>
            {
                new("design.documents",       "Belge Sablonlari",     "FileText",   "/Document",              null),
                new("design.guides",          "Rehber Yonetimi",      "BookOpen",   "/Admin/GuideManagement", null),
                new("design.formmanagement",  "Form Tasarim Ayarlari","LayoutList", "/Admin/FormManagement",  null),
            }),

            // ────────────── 8. Ayarlar ──────────────
            new("settings", "Ayarlar", "Settings", null, settingsChildren),
        };
    }
}
