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
    /// <remarks>
    /// MatchPath: opsiyonel URL prefix. Set ise menu tiklandiginda Shell mevcut
    /// tab'lar arasinda bu prefix'e sahip olani arar; varsa o tab'i AS-IS aktive eder
    /// (URL'i degistirmez — kullanicinin edit context'i korunur). Yoksa Url ile yeni
    /// tab acar. Ornek: Malzeme Kartlari menusu icin matchPath='/Logistics/MaterialCard'
    /// ile WO ekranindan acilan MaterialCardEdit?id=N tab'i bulunup aktif edilir.
    /// </remarks>
    public sealed record MenuNode(
        string Key,                          // Unique — "logistics.materials"
        string Label,                        // Kullaniciya gosterilen metin
        string? Icon,                        // Lucide icon adi (null ise CircleDot fallback)
        string? Url,                         // Tiklaninca acilacak URL — null ise sadece grup basligi
        IReadOnlyList<MenuNode>? Children,   // Nested alt ogeler
        string? MatchPath = null);           // Opsiyonel mevcut tab match prefix

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
            new("settings.company",           "Şirket Ayarları",         "Building2",   "/Admin/CompanySettings",    null),
            new("settings.parameters",        "Şirket Parametreleri",    "SlidersHorizontal", "/Admin/Parameters",   null),
            // Tum entegrasyon ekranlari (Profiller / Endpointler / Entegrasyonlar / Aktarim Kuyrugu
            // / Enum Tanimlari / Calistirma Logu) tek sayfada — IntegrationsHub icindeki tab'larla.
            // Deep-link: /Integrations#queue, /Integrations#enums vb.
            new("settings.integrations",      "Entegrasyon Wizard",      "Plug",        "/Integrations",             null,
                MatchPath: "/Integrations"),
            new("settings.viewsettings",      "Alan Rehberi",            "LayoutGrid",  "/Admin/ViewSettings",       null),
            new("settings.dbschema",          "Veritabanı Haritası",     "Database",    "/admin/db-schema",          null),
            new("settings.scheduledtasks",    "Zamanlanmış Görevler",    "Clock",       "/Admin/ScheduledTasks",     null),
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
                new("general.whatsapp", "WhatsApp", "MessageCircle", "/Whatsapp", null),
            }),

            // ────────────── 2. Raporlar ──────────────
            new("reports", "Raporlar", "BarChart3", null, new List<MenuNode>
            {
                new("reports.dashboards", "Panolar", "BarChart3", "/Dashboard", null),
                new("reports.grafana", "Grafana", "Activity", "/Dashboard/Grafana", null),
            }),

            // ────────────── 3. Onay Islemleri ──────────────
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
                    new("logistics.materials", "Malzeme Kartları", "Boxes", "/Logistics/MaterialCards", null,
                        MatchPath: "/Logistics/MaterialCard"),
                    new("logistics.configuration", "Özellik ve Kombinasyon", "Sliders", "/Logistics/ProductConfiguration", null),
                    new("logistics.combinations", "Tanımlı Kombinasyonlar", "Grid3X3", "/Logistics/Combinations", null,
                        MatchPath: "/Logistics/Combination"),
                }),
                new("logistics.sales", "Satış", "TrendingUp", null, new List<MenuNode>
                {
                    new("logistics.salesquotes", "Satış Teklifi", "FileText", "/Sales/Documents", null),
                    new("logistics.salesorders", "Satış Siparişi", "ShoppingCart", "/Sales/Orders", null),
                }),
                new("logistics.warehouse", "Depo", "Warehouse", null, new List<MenuNode>
                {
                    new("logistics.transfer",   "Transfer",             "ArrowLeftRight", "/Warehouse/Transfer",   null),
                    new("logistics.stockentry", "Ambar Giriş / Çıkış", "PackageCheck",   "/Warehouse/StockEntry", null),
                }),
            }),

            // ────────────── 4. Uretim ──────────────
            new("production", "Üretim", "Factory", null, new List<MenuNode>
            {
                new("production.tree",       "Ürün Ağacı",      "Network",      "/Logistics/BOMs",          null),
                new("production.workorders", "İş Emirleri",     "ClipboardList","/Production/WorkOrders",   null),
                new("production.shopfloor",  "Üretim Terminali","Tablet",       "/Production/ShopFloor",    null),
            }),

            // ────────────── 5. Finans ──────────────
            new("finance", "Finans", "Coins", null, new List<MenuNode>
            {
                new("finance.definitions", "Tanımlamalar", "Folder", null, new List<MenuNode>
                {
                    new("finance.accounts", "Cari Hesaplar", "Users", "/Finance/Contacts", null),
                }),
            }),

            // ────────────── 6. Tanimlamalar ──────────────
            new("generaldefs", "Tanımlamalar", "Settings2", null, new List<MenuNode>
            {
                new("gendef.salesreps",   "Satış Temsilcileri",         "UserCircle",  "/GeneralDefinitions/SalesRepresentatives", null),
                new("gendef.currencies",  "Döviz Tanımlamaları",        "DollarSign",  "/GeneralDefinitions/Currencies",           null),
                new("gendef.warehouses",  "Lokasyon Tanımlamaları",     "MapPin",      "/Logistics/Locations",            null),
                new("gendef.measureunit", "Ölçü Birimleri",  "Ruler",       "/Logistics/Units",        null),
                new("gendef.cardgroups",  "Grup Tanımlamaları",         "Layers",      "/Definitions/CardGroups",                  null),
                new("gendef.machines",    "Makine Tanımlamaları",       "Cog",         "/Logistics/Machines",                      null),
                new("gendef.operations",  "Operasyon Tanımlamaları",    "Hammer",      "/Production/Operations",                   null),
                new("gendef.routings",    "Rota Tanımlamaları",         "Workflow",    "/Production/Routings",                     null),
                new("gendef.departments", "Departman Tanımlamaları",    "Building2",   "/Admin/Departments",                       null),
                new("gendef.personnel",   "Personel Tanımlamaları",     "Users",       "/Production/Personnel",                    null),
                new("gendef.pricelist",   "Fiyat Listesi",              "Tag",         "/PriceList/PriceGroups",                   null),
            }),

            // ────────────── 7. Tasarim ──────────────
            new("design", "Tasarım", "LayoutGrid", null, new List<MenuNode>
            {
                new("design.docdesigner",     "Belge Tasarımcısı",     "PenSquare",  "/DocDesigner",           null),
                new("design.doclayoutrule",   "Tasarım Kuralları",     "GitBranch",  "/DocLayoutRule",         null),
                // Rehber Yonetimi menusu kaldirildi — rehber konfigurasyonu artik
                // widget bazinda inline (WidgetBuilderForm'daki "Rehber" satiri +
                // GuideSettingsModal) ya da text+rehber butonu uzerinden yapiliyor.
            }),

            // ────────────── 8. Ayarlar ──────────────
            new("settings", "Ayarlar",  "Settings", null, settingsChildren),
        };
    }
}
