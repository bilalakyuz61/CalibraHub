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
            new("settings.integrationevents", "Entegrasyon Tanımları",  "Zap",         "/Admin/IntegrationEvents",  null),
            new("settings.viewsettings",      "Alan ve Widget Tanımları", "LayoutGrid",  "/Admin/ViewSettings",       null),
            new("settings.dbschema",          "Veritabanı Haritası",    "Database",    "/admin/db-schema",          null),
            new("settings.scheduledtasks",    "Zamanlanmış Görevler",   "Clock",       "/Admin/ScheduledTasks",     null),
        };
        if (isSystemAdmin)
        {
            settingsChildren.Add(
                new("settings.setupdefinitions", "Şirket ve Kullanıcı Tanımları", "UserCog", "/Setup/Definitions", null));
        }

        return new List<MenuNode>
        {
            // ────────────── 1. Genel ──────────────
            new("general", "Genel", "LayoutList", null, new List<MenuNode>
            {
                new("general.notes", "Notlar", "FileText", "/Notes", null),
                new("general.orgchart", "Organizasyon Şeması", "Network", "/OrgChart", null),
            }),

            // ────────────── 2. Onay Islemleri ──────────────
            new("approval", "Onay İşlemleri", "CheckCircle2", null, new List<MenuNode>
            {
                new("approval.documents", "Elektronik Belgeler", "Files", null, new List<MenuNode>
                {
                    new("approval.einvoice",  "e-Fatura",   "FileText", "/Approval/EInvoice",  null),
                    new("approval.earchive",  "e-Arşiv",    "Archive",  "/Approval/EArchive",  null),
                    new("approval.edispatch", "e-İrsaliye", "Truck",    "/Approval/EDispatch", null),
                }),
            }),

            // ────────────── 3. Lojistik ──────────────
            new("logistics", "Lojistik", "Package", null, new List<MenuNode>
            {
                new("logistics.fixed", "Sabit Tanımlamalar", "Folder", null, new List<MenuNode>
                {
                    new("logistics.materials", "Malzeme Kartları", "Boxes", "/Logistics/MaterialCards", null),
                    new("logistics.configuration", "Özellik ve Kombinasyon", "Sliders", "/Logistics/ProductConfiguration", null),
                }),
                new("logistics.sales", "Satış", "TrendingUp", null, new List<MenuNode>
                {
                    new("logistics.salesquotes", "Satış Teklifi", "FileText", "/Sales/Documents", null),
                }),
            }),

            // ────────────── 4. Uretim ──────────────
            new("production", "Üretim", "Factory", null, new List<MenuNode>
            {
                new("production.tree", "Ürün Ağacı", "Network", "/Logistics/BOMs", null),
            }),

            // ────────────── 5. Finans ──────────────
            new("finance", "Finans", "Coins", null, new List<MenuNode>
            {
                new("finance.definitions", "Tanımlamalar", "Folder", null, new List<MenuNode>
                {
                    new("finance.accounts", "Cari Hesaplar", "Users", "/Finance/Contacts", null),
                }),
            }),

            // ────────────── 6. Genel Tanimlamalar ──────────────
            new("generaldefs", "Genel Tanımlamalar", "Settings2", null, new List<MenuNode>
            {
                new("gendef.salesreps",   "Satış Temsilcileri",         "UserCircle",  "/GeneralDefinitions/SalesRepresentatives", null),
                new("gendef.currencies",  "Döviz Tanımlamaları",        "DollarSign",  "/GeneralDefinitions/Currencies",           null),
                new("gendef.warehouses",  "Lokasyon Tanımlamaları",     "MapPin",      "/Logistics/Locations",            null),
                new("gendef.measureunit", "Ölçü Birimleri",  "Ruler",       "/Logistics/Units",        null),
                new("gendef.cardgroups",  "Grup Tanımlamaları",         "Layers",      "/Definitions/CardGroups",                  null),
                new("gendef.pricelist",   "Fiyat Listesi",              "Tag",         "/PriceList/PriceGroups",                   null),
            }),

            // ────────────── 7. Tasarim ──────────────
            new("design", "Tasarım", "LayoutGrid", null, new List<MenuNode>
            {
                new("design.documents",       "Belge Şablonları",      "FileText",   "/Document",              null),
                new("design.guides",          "Rehber Yönetimi",       "BookOpen",   "/Admin/GuideManagement", null),
                new("design.formmanagement",  "Form Tasarım Ayarları", "LayoutList", "/Admin/FormManagement",  null),
            }),

            // ────────────── 8. Ayarlar ──────────────
            new("settings", "Ayarlar",  "Settings", null, settingsChildren),
        };
    }
}
