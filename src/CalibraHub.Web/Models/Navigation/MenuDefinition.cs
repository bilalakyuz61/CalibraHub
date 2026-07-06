using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Web.Models.Navigation;

/// <summary>
/// React Shell (glassmorphism kabuk) icin ana navigasyon menusunun
/// static tanimi. <c>_MainMenu.cshtml</c> icerigini bire bir yansitir —
/// ayni 8 grup, ayni alt ogeler, ayni rotalar.
///
/// Kullanim (Views/Shared/_Layout.cshtml icinde):
///     var menu = MenuDefinition.GetMainMenu(isSystemAdmin, languageCode);
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
    ///
    /// PermissionFormCode: set ise VIEW yetkisi yoksa node filtrelenir.
    /// AdminOnly: true ise sadece SystemAdmin + DepartmentManager gorur (VIEW yetkisinden bagimsiz).
    /// </remarks>
    public sealed record MenuNode(
        string Key,                          // Unique — "logistics.materials"
        string Label,                        // Kullaniciya gosterilen metin
        string? Icon,                        // Lucide icon adi (null ise CircleDot fallback)
        string? Url,                         // Tiklaninca acilacak URL — null ise sadece grup basligi
        IReadOnlyList<MenuNode>? Children,   // Nested alt ogeler
        string? MatchPath = null,            // Opsiyonel mevcut tab match prefix
        string? PermissionFormCode = null,   // VIEW izni yoksa menüde gizle
        bool AdminOnly = false);             // Yalnizca SystemAdmin + DepartmentManager gorur

    /// <summary>
    /// Tam menu agacini doner.
    /// </summary>
    /// <param name="isSystemAdmin">
    /// admin@calibra.local ise Setup/Definitions ogesi gosterilir
    /// (mevcut _MainMenu.cshtml'deki conditional aynen korunur).
    /// </param>
    /// <param name="languageCode">
    /// "en-US" verilirse menu etiketleri Ingilizce doner; diger tum degerler Turkce.
    /// </param>
    public static IReadOnlyList<MenuNode> GetMainMenu(bool isSystemAdmin, string? languageCode = null)
    {
        var isEn = string.Equals(languageCode, "en-US", StringComparison.OrdinalIgnoreCase);

        var settingsChildren = new List<MenuNode>
        {
            new("settings.company",        isEn ? "Company Settings"       : "Şirket Ayarları",         "Building2",          "/Admin/CompanySettings",    null, AdminOnly: true),
            new("settings.parameters",     isEn ? "Company Parameters"     : "Şirket Parametreleri",    "SlidersHorizontal",  "/Admin/Parameters",         null, AdminOnly: true),
            new("settings.decimals",       isEn ? "Decimal Settings"       : "Ondalık Ayarları",        "Ruler",              "/Admin/DecimalSettings",    null,
                PermissionFormCode: FormCodes.DecimalSettings),
            new("settings.integrations",   isEn ? "Integration Wizard"     : "Entegrasyon Wizard",      "Plug",               "/Integrations",             null,
                MatchPath: "/Integrations", PermissionFormCode: FormCodes.Integrations),
            new("settings.viewsettings",   isEn ? "Field Guide"            : "Alan Rehberi",            "LayoutGrid",         "/Admin/ViewSettings",       null,
                PermissionFormCode: FormCodes.SetupDefinitions),
            new("settings.companyusers",   isEn ? "User Definitions"       : "Kullanıcı Tanımlamaları", "Users",              "/CompanyUser",              null, AdminOnly: true),
            new("settings.permissions",    isEn ? "Permission Management"  : "Yetki Yönetimi",          "ShieldCheck",        "/Admin/Permissions",        null,
                PermissionFormCode: FormCodes.PermissionMgmt),
            new("settings.datavisibility", isEn ? "Data Visibility Rules"  : "Veri Perdeleme Kuralları","EyeOff",             "/Admin/DataVisibilityRules",null,
                PermissionFormCode: FormCodes.DataVisibility),
            new("settings.dbschema",       isEn ? "Database Map"           : "Veritabanı Haritası",     "Database",           "/admin/db-schema",          null, AdminOnly: true),
            new("settings.scheduledtasks", isEn ? "Scheduled Tasks"        : "Zamanlanmış Görevler",    "Clock",              "/Admin/ScheduledTasks",     null,
                PermissionFormCode: FormCodes.Scheduler),
        };
        if (isSystemAdmin)
        {
            settingsChildren.Add(
                new("settings.healthcheck", isEn ? "System Health Check" : "Sistem Sağlık Kontrolü", "Activity", "/Admin/HealthCheck", null));
            settingsChildren.Add(
                new("settings.gateadmin",   isEn ? "System Management"   : "Sistem Yönetimi",         "ShieldCheck", "/Gate", null,
                    MatchPath: "/Gate"));
        }

        return new List<MenuNode>
        {
            // ────────────── 1. Genel / General ──────────────
            // Grup: isMenuAdmin || canView("NOTES")  →  en az bir child gerekiyor (otomatik gizleme var)
            new("general", isEn ? "General" : "Genel", "LayoutList", null, new List<MenuNode>
            {
                new("general.calendar", isEn ? "Calendar"           : "Takvim",              "CalendarDays", "/Calendar", null,
                    PermissionFormCode: FormCodes.Calendar),
                new("general.notes",    isEn ? "Notes"              : "Notlar",             "FileText",     "/Notes",    null,
                    PermissionFormCode: FormCodes.Notes),
                new("general.whatsapp", "WhatsApp",                                          "MessageCircle","/Whatsapp", null,
                    PermissionFormCode: FormCodes.WhatsApp),
                new("general.mailsend", isEn ? "Bulk Mail"          : "Toplu Mail",          "Mail",         "/MailSend", null,
                    PermissionFormCode: FormCodes.BulkMail),
            }),

            // ────────────── 2. Raporlar / Reports ──────────────
            // Grup: canViewDashboards || isMenuAdmin  →  tüm içerik admin-only veya dashboard yetkili
            new("reports", isEn ? "Reports" : "Raporlar", "BarChart3", null, new List<MenuNode>
            {
                new("reports.boards",     isEn ? "Report Boards"   : "Rapor Panoları",   "LayoutDashboard", "/Dashboard/Boards",   null,
                    PermissionFormCode: FormCodes.Dashboards),
                new("reports.dashboards", isEn ? "Report Designer" : "Rapor Tasarımcısı", "PenLine",        "/Dashboard/Designer", null,
                    PermissionFormCode: FormCodes.ReportDesigner),
            }),

            // ────────────── 3. Onay İşlemleri / Approval Processes ──────────────
            // Grup: @if (isMenuAdmin)  →  tüm içerik admin-only
            new("approval", isEn ? "Approval Processes" : "Onay İşlemleri", "CheckCircle2", null, new List<MenuNode>
            {
                new("approval.pending",   isEn ? "Pending Approvals"       : "Onayda Bekleyenler",   "Inbox",    "/PendingApproval", null,
                    PermissionFormCode: FormCodes.ApprovalPending),
                new("approval.documents", isEn ? "Electronic Documents"    : "Elektronik Belgeler",  "Files",    null, new List<MenuNode>
                {
                    new("approval.einvoice",  isEn ? "e-Invoice"  : "e-Fatura",   "FileText", "/Approval/EInvoice",  null,
                        PermissionFormCode: FormCodes.EInvoice),
                    new("approval.earchive",  isEn ? "e-Archive"  : "e-Arşiv",    "Archive",  "/Approval/EArchive",  null,
                        PermissionFormCode: FormCodes.EArchive),
                    new("approval.edispatch", isEn ? "e-Dispatch" : "e-İrsaliye", "Truck",    "/Approval/EDispatch", null,
                        PermissionFormCode: FormCodes.EDispatch),
                }),
                new("approval.flows", isEn ? "Approval Flow Definitions" : "Onay Akış Tanımları", "GitBranch", "/ApprovalFlow", null,
                    PermissionFormCode: FormCodes.ApprovalFlows),
            }),

            // ────────────── 4. Lojistik / Logistics ──────────────
            // Grup: isMenuAdmin || canView("MATERIAL_CARD_EDIT") || canView("PRODUCT_CONFIG") || canView("SALES_QUOTE")
            new("logistics", isEn ? "Logistics" : "Lojistik", "Package", null, new List<MenuNode>
            {
                // Sabit Tanımlamalar: canView("MATERIAL_CARD_EDIT") || canView("PRODUCT_CONFIG")
                new("logistics.fixed", isEn ? "Fixed Definitions" : "Sabit Tanımlamalar", "Folder", null, new List<MenuNode>
                {
                    new("logistics.materials",     isEn ? "Material Cards"          : "Malzeme Kartları",          "Boxes",   "/Logistics/MaterialCards",         null,
                        MatchPath: "/Logistics/MaterialCard",
                        PermissionFormCode: FormCodes.MaterialCardEdit),
                    new("logistics.configuration", isEn ? "Features & Combinations" : "Özellik ve Kombinasyon",    "Sliders", "/Logistics/ProductConfiguration",  null,
                        PermissionFormCode: FormCodes.ProductConfig),
                    new("logistics.combinations",  isEn ? "Defined Combinations"    : "Tanımlı Kombinasyonlar",    "Grid3X3", "/Logistics/Combinations",          null,
                        MatchPath: "/Logistics/Combination",
                        PermissionFormCode: FormCodes.ProductConfig),
                }),
                // Satış: canView("SALES_QUOTE") / canView("SALES_ORDER")
                new("logistics.sales", isEn ? "Sales" : "Satış", "TrendingUp", null, new List<MenuNode>
                {
                    new("logistics.salesquotes", isEn ? "Sales Quote" : "Satış Teklifi",  "FileText",    "/Sales/Quotes", null,
                        PermissionFormCode: FormCodes.SalesQuote),
                    new("logistics.salesorders", isEn ? "Sales Order" : "Satış Siparişi", "ShoppingCart","/Sales/Orders", null,
                        PermissionFormCode: FormCodes.SalesOrder),
                }),
                // Satın Alma
                new("logistics.purchase", isEn ? "Purchase" : "Satın Alma", "ShoppingBag", null, new List<MenuNode>
                {
                    new("logistics.purchaserequests",    isEn ? "Purchase Request"    : "İhtiyaç Kaydı",        "ClipboardList",  "/Purchase/Requests",          null,
                        PermissionFormCode: FormCodes.PurchaseRequest),
                    new("logistics.purchasefulfillment", isEn ? "Fulfillment Center" : "İhtiyaç Karşılama",  "PackageCheck",   "/Purchase/FulfillmentCenter", null,
                        PermissionFormCode: FormCodes.PurchaseFulfillment),
                    new("logistics.purchasequotes",      isEn ? "Purchase Quote"     : "Satın Alma Teklif",  "FileText",       "/Purchase/Quotes",            null,
                        PermissionFormCode: FormCodes.PurchaseQuote),
                    new("logistics.purchaseorders",   isEn ? "Purchase Order"   : "Satın Alma Sipariş", "ShoppingCart",  "/Purchase/Orders",   null,
                        PermissionFormCode: FormCodes.PurchaseOrder),
                }),
                // Depo
                new("logistics.warehouse", isEn ? "Warehouse" : "Depo", "Warehouse", null, new List<MenuNode>
                {
                    new("logistics.transfer",       isEn ? "Transfer"          : "Transfer",              "ArrowLeftRight", "/Warehouse/Transfer",   null,
                        PermissionFormCode: FormCodes.Transfer),
                    new("logistics.stockentry",     isEn ? "Stock Entry/Exit"  : "Ambar Giriş / Çıkış",  "PackageCheck",   "/Warehouse/StockEntry", null,
                        PermissionFormCode: FormCodes.StockIn),
                    new("logistics.inventorycount", isEn ? "Inventory Count"   : "Sayım",                 "ClipboardCheck", "/Warehouse/Inventory",  null,
                        PermissionFormCode: FormCodes.InventoryCount),
                }),
            }),

            // ────────────── 5. Üretim / Production ──────────────
            // Grup: isMenuAdmin || canView("BOM_EDIT")
            new("production", isEn ? "Production" : "Üretim", "Factory", null, new List<MenuNode>
            {
                new("production.tree",        isEn ? "BOM / Product Tree"        : "Ürün Ağacı",             "Network",       "/Logistics/BOMs",          null,
                    PermissionFormCode: FormCodes.BomEdit),
                new("production.workorders",  isEn ? "Work Orders"               : "İş Emirleri",            "ClipboardList", "/Production/WorkOrders",   null,
                    PermissionFormCode: FormCodes.WorkOrders),
                new("production.shopfloor",   isEn ? "Production Terminal"       : "Üretim Terminali",       "Tablet",        "/Production/ShopFloor",    null,
                    PermissionFormCode: FormCodes.ShopFloor),
                new("production.definitions", isEn ? "Production Definitions"    : "Üretim Tanımlamaları",   "Settings2",     "/Production/Definitions",  null,
                    PermissionFormCode: FormCodes.ProductionDefs),
            }),

            // ────────────── AR-GE / R&D ──────────────
            // Grup: AR-GE projeleri (Document tabanli 'arge_proje' tipi). PermissionFormCode
            // controller'daki [PermissionScope("ARGE_PROJECT_EDIT")] ile ayni — non-admin'de
            // VIEW yetkisi yoksa gizlenir, SystemAdmin/DepartmentManager her zaman gorur.
            new("arge", isEn ? "R&D / NPD" : "AR-GE / ÜR-GE", "FlaskConical", null, new List<MenuNode>
            {
                new("arge.projects", isEn ? "Projects" : "Projeler", "FlaskConical", "/Arge/Projects", null,
                    MatchPath: "/Arge/Project", PermissionFormCode: "ARGE_PROJECT_EDIT"),
            }),

            // ────────────── Varlık Yönetimi / Asset Management ──────────────
            new("assets", isEn ? "Asset Management" : "Varlık Yönetimi", "Boxes", null, new List<MenuNode>
            {
                new("assets.list", isEn ? "Asset List" : "Varlık Listesi", "ClipboardList", "/Assets", null,
                    MatchPath: "/Assets", PermissionFormCode: FormCodes.Assets),
            }),

            // ────────────── Döküman Yönetimi / Document Management ──────────────
            new("docmgmt", isEn ? "Document Management" : "Döküman Yönetimi", "FileStack", null, new List<MenuNode>
            {
                new("docmgmt.list", isEn ? "All Documents" : "Tüm Dökümanlar", "Files", "/DocumentManagement", null,
                    MatchPath: "/DocumentManagement", PermissionFormCode: FormCodes.DocumentManagement),
            }),

            // ────────────── 6. Finans / Finance ──────────────
            // Grup: canView("CONTACTS")
            new("finance", isEn ? "Finance" : "Finans", "Coins", null, new List<MenuNode>
            {
                new("finance.definitions", isEn ? "Definitions" : "Tanımlamalar", "Folder", null, new List<MenuNode>
                {
                    new("finance.accounts", isEn ? "Accounts" : "Cari Hesaplar", "Users", "/Finance/Contacts", null,
                        PermissionFormCode: FormCodes.Contacts),
                }),
            }),

            // ────────────── 7. Tanımlamalar / Definitions ──────────────
            new("generaldefs", isEn ? "Definitions" : "Tanımlamalar", "Settings2", null, new List<MenuNode>
            {
                new("gendef.general",     isEn ? "General Definitions"    : "Genel Tanımlamalar",      "BookOpen",    "/GeneralDefs/Countries", null,
                    MatchPath: "/GeneralDefs", PermissionFormCode: FormCodes.GeneralDefs),
                new("gendef.salesreps",   isEn ? "Sales Representatives"  : "Satış Temsilcileri",      "UserCircle",  "/GeneralDefinitions/SalesRepresentatives", null,
                    PermissionFormCode: FormCodes.SalesReps),
                new("gendef.currencies",  isEn ? "Currency Definitions"   : "Döviz Tanımlamaları",     "DollarSign",  "/GeneralDefinitions/Currencies",           null,
                    PermissionFormCode: FormCodes.Currencies),
                new("gendef.warehouses",  isEn ? "Location Definitions"   : "Lokasyon Tanımlamaları",  "MapPin",      "/Logistics/Locations",                     null,
                    PermissionFormCode: FormCodes.Locations),
                new("gendef.measureunit", isEn ? "Measure Units"          : "Ölçü Birimleri",          "Ruler",       "/Logistics/Units",                         null,
                    PermissionFormCode: FormCodes.MeasureUnits),
                new("gendef.cardgroups",  isEn ? "Group Definitions"      : "Grup Tanımlamaları",      "Layers",      "/Definitions/CardGroups",                  null,
                    PermissionFormCode: FormCodes.CardGroups),
                new("gendef.departments", isEn ? "Department Definitions" : "Departman Tanımlamaları", "Building2",   "/Admin/Departments",                       null,
                    PermissionFormCode: FormCodes.Departments),
                new("gendef.pricelist",   isEn ? "Price List"             : "Fiyat Listesi",           "Tag",         "/PriceList/PriceGroups",                   null,
                    PermissionFormCode: FormCodes.PriceList),
            }),

            // ────────────── Veri Aktarımı / Data Import ──────────────
            // Şablon-tabanlı içe aktarım (AI'sız). 2026-07-06: DATA_IMPORT form kodu ile
            // yetki kapsamına alındı — toplu veri yazma yetkisiz kullanıcıya açık kalmamalı.
            new("dataimport", isEn ? "Data Import" : "Veri Aktarımı", "Upload", null, new List<MenuNode>
            {
                new("dataimport.run", isEn ? "Import / Templates" : "İçe Aktarım", "FileUp", "/Import", null,
                    MatchPath: "/Import", PermissionFormCode: FormCodes.DataImport),
            }),

            // ────────────── 8. Tasarım / Design ──────────────
            // Grup: @if (isMenuAdmin)  →  tüm içerik admin-only
            new("design", isEn ? "Design" : "Tasarım", "LayoutGrid", null, new List<MenuNode>
            {
                new("design.docdesigner",   isEn ? "Document Designer" : "Belge Tasarımcısı", "PenSquare", "/DocDesigner",   null,
                    PermissionFormCode: FormCodes.DocTemplates),
                new("design.doclayoutrule", isEn ? "Design Rules"      : "Tasarım Kuralları", "GitBranch", "/DocLayoutRule", null,
                    PermissionFormCode: FormCodes.DocLayoutRules),
            }),

            // ────────────── 9. Ayarlar / Settings ──────────────
            // Grup: isMenuAdmin || canView("SETUP_DEFINITIONS") || canView("PERMISSION_MGMT")
            new("settings", isEn ? "Settings" : "Ayarlar", "Settings", null, settingsChildren),
        };
    }

    /// <summary>
    /// Kullanıcı VIEW yetkisi olmayan veya AdminOnly olan menü öğelerini ağaçtan budar.
    /// <list type="bullet">
    ///   <item><see cref="MenuNode.PermissionFormCode"/> dolu öğeler VIEW yetkisi yoksa gizlenir.</item>
    ///   <item><see cref="MenuNode.AdminOnly"/> = true öğeler yalnızca SystemAdmin + DepartmentManager'a gösterilir.</item>
    ///   <item>Grup düğümü (Url=null) ve tüm child'lar elendiyse → grubu da gizle.</item>
    /// </list>
    /// </summary>
    public static async Task<IReadOnlyList<MenuNode>> FilterByPermissionAsync(
        IReadOnlyList<MenuNode> menu,
        IPermissionService permService,
        int userId, UserRole role, int? departmentId,
        CancellationToken ct)
    {
        // SystemAdmin tüm menüyü görür — DB sorgusu yok
        if (role == UserRole.SystemAdmin) return menu;

        // DepartmentManager de admin sayılır → AdminOnly öğeleri görebilir
        var isAdmin = role == UserRole.DepartmentManager;

        var result = new List<MenuNode>(menu.Count);
        foreach (var node in menu)
        {
            // AdminOnly filtresi: admin kullanıcılar geçer
            if (node.AdminOnly && !isAdmin) continue;

            // Permission gerekiyorsa kontrol et — admin kullanıcılar bu kontrolü de atlar.
            // Önce standart CRUD action'larına bak; bulunamazsa (örn. DASHBOARDS gibi tüm standart
            // action'ları inactive olan formlar için) form genelindeki aktif action'lardan herhangi
            // birini kontrol et — RESOURCE:DASH:* gibi non-standart action'lar da kapsanır.
            if (!string.IsNullOrEmpty(node.PermissionFormCode) && !isAdmin)
            {
                // Özel/Departman/Genel tüm seviyeleri dahil; herhangi biri yeterliyse menüde göster.
                var allowed = await permService.CheckAnyAsync(
                    userId, role, departmentId, node.PermissionFormCode,
                    new[] { "VIEW", "VIEW_DEPT", "VIEW_OWN", "CREATE",
                            "EDIT_OWN", "EDIT_DEPT", "EDIT_ALL",
                            "DELETE_OWN", "DELETE_DEPT", "DELETE_ALL" }, ct);
                if (!allowed)
                    allowed = await permService.CheckAnyForFormAsync(
                        userId, role, departmentId, node.PermissionFormCode, ct);
                if (!allowed) continue;
            }

            // Children için recursive
            IReadOnlyList<MenuNode>? filteredChildren = null;
            if (node.Children is { Count: > 0 })
            {
                filteredChildren = await FilterByPermissionAsync(
                    node.Children, permService, userId, role, departmentId, ct);
                // Grup düğümü (Url=null) ve tüm child'lar elendiyse → grubu da gizle
                if (string.IsNullOrEmpty(node.Url) && filteredChildren.Count == 0)
                    continue;
            }

            result.Add(node with { Children = filteredChildren });
        }
        return result;
    }

    /// <summary>
    /// Faz 3 dual-track: DB item listesini statik ağaca enjekte eder.
    /// Her DB item'ı için MenuKey ile eşleşen statik node bulunur;
    /// bulunan node'un Label/Url/MatchPath değerleri DB'den güncellenir.
    /// AdminOnly node'lar ve yapı (grup container'lar) değişmez.
    /// </summary>
    public static IReadOnlyList<MenuNode> InjectDbItems(
        IReadOnlyList<MenuNode> staticTree,
        IReadOnlyList<CalibraHub.Application.Contracts.MenuItemDto> dbItems,
        bool isEn)
    {
        if (dbItems.Count == 0) return staticTree;
        var byKey = dbItems.ToDictionary(x => x.MenuKey, StringComparer.OrdinalIgnoreCase);
        return ReplaceNodes(staticTree, byKey, isEn);
    }

    private static IReadOnlyList<MenuNode> ReplaceNodes(
        IReadOnlyList<MenuNode> nodes,
        Dictionary<string, CalibraHub.Application.Contracts.MenuItemDto> byKey,
        bool isEn)
    {
        var result = new List<MenuNode>(nodes.Count);
        foreach (var node in nodes)
        {
            // Çocukları recursive güncelle
            IReadOnlyList<MenuNode>? updatedChildren = null;
            if (node.Children is { Count: > 0 })
                updatedChildren = ReplaceNodes(node.Children, byKey, isEn);

            // Bu node DB'de varsa label/url/matchPath güncelle
            if (!string.IsNullOrEmpty(node.Key) && byKey.TryGetValue(node.Key, out var db))
            {
                var label = isEn && !string.IsNullOrEmpty(db.MenuLabelEn)
                    ? db.MenuLabelEn
                    : db.MenuLabel;
                result.Add(node with
                {
                    Label     = label,
                    Url       = db.Url ?? node.Url,
                    MatchPath = db.MatchPath ?? node.MatchPath,
                    Children  = updatedChildren,
                });
            }
            else
            {
                result.Add(node with { Children = updatedChildren });
            }
        }
        return result;
    }

    /// <summary>
    /// Faz 4: GetMainMenu() artık kullanılmamalıdır.
    /// MenuService.GetMenuItemsAsync() + InjectDbItems() kullanın.
    /// </summary>
    [Obsolete("Faz 4 (2026-06-09): IMenuService.GetMenuItemsAsync() + MenuDefinition.InjectDbItems() kullanın.")]
    public static IReadOnlyList<MenuNode> GetMainMenuLegacy(bool isSystemAdmin, string? languageCode = null)
        => GetMainMenu(isSystemAdmin, languageCode);
}
