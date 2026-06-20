using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services.Dashboard;

/// <summary>
/// 2026-06-14 — Ana Sayfa Panosu widget kataloğu (statik, kod-tanımlı).
///
/// Hangi widget'ların var olduğu, izin kapısı (FormCode), varsayılan boyut ve
/// varsayılan sıra burada tek kaynaktan tanımlanır. MenuDefinition / FormCodes
/// ile aynı yaklaşım — DB tablosu eklemek ~8 sistem widget'ı için migration +
/// admin-UI yükü getirir, per-tenant değer sağlamaz (per-company DB, CompanyId yok).
///
/// Kullanıcı tanımlı özel widget gerekirse o zaman tablo eklenir; şimdilik kod registry.
/// </summary>
public static class DashboardWidgetCatalog
{
    /// <summary>Tek bir katalog girdisi — widget → FormCode kapısı eşlemesini taşır.</summary>
    public sealed record CatalogEntry(
        string Type, string Title, string Description, string Icon, string IconColor,
        string DefaultSize, bool AllowMultiple,
        string? PermissionFormCode,          // null = her zaman görünür (kapı yok)
        string[] PermissionActions);         // OR-kontrol edilecek action'lar

    /// <summary>
    /// MenuDefinition.FilterByPermissionAsync ile BİREBİR aynı OR-action seti —
    /// böylece bir widget tam karşılık gelen menü öğesi göründüğünde görünür.
    /// </summary>
    public static readonly string[] ViewActions =
    {
        "VIEW", "CREATE", "EDIT_OWN", "EDIT_ALL", "DELETE_OWN", "DELETE_ALL",
    };

    public static readonly IReadOnlyList<CatalogEntry> All = new[]
    {
        new CatalogEntry("welcome-card",      "Hoş Geldiniz",       "Kullanıcı ve şirket kartı",        "UserCircle",   "indigo",  "lg", false, null,                       ViewActions),
        new CatalogEntry("quick-links",       "Kısayollar",         "Sık kullanılan ekranlara erişim",  "Zap",          "amber",   "md", true,  null,                       ViewActions),
        new CatalogEntry("pending-approvals", "Onayda Bekleyenler", "Onayınızı bekleyen belge sayısı",  "Inbox",        "rose",    "sm", false, FormCodes.ApprovalPending,  ViewActions),
        new CatalogEntry("exchange-rates",    "Döviz Kurları",      "Güncel döviz kurları",             "DollarSign",   "emerald", "md", false, FormCodes.Currencies,       ViewActions),
        new CatalogEntry("recent-documents",  "Son Belgeler",       "Son işlem gören belgeler",         "Files",        "blue",    "md", false, FormCodes.SalesQuote,       ViewActions),
        new CatalogEntry("work-orders",       "İş Emirleri Özeti",  "Durumlara göre iş emri sayısı",    "ClipboardList","violet",  "md", false, FormCodes.WorkOrders,       ViewActions),
        new CatalogEntry("sales-quotes",      "Satış Teklifleri",   "Teklif durum özeti",               "FileText",     "indigo",  "md", false, FormCodes.SalesQuote,       ViewActions),
        new CatalogEntry("stock-alerts",      "Stok Uyarıları",     "Minimum altı stoklar",             "PackageX",     "amber",   "md", false, FormCodes.InventoryCount,   ViewActions),
        new CatalogEntry("calendar",           "Takvim",             "Aylık takvim görünümü",             "CalendarDays", "violet",  "md", false, null,                       ViewActions),
    };

    /// <summary>Tip ile katalog girdisini bulur (büyük/küçük harf duyarsız).</summary>
    public static CatalogEntry? Find(string type) =>
        All.FirstOrDefault(e => string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Kullanıcının saklı layout'u yokken kullanılacak varsayılan sıralı liste (tek sayfa için).
    /// İzin filtresine GetConfigAsync içinde ayrıca tabi tutulur.
    /// </summary>
    public static IReadOnlyList<DashboardWidgetInstanceDto> DefaultLayout() => new[]
    {
        new DashboardWidgetInstanceDto("welcome-card",      "lg", null),
        new DashboardWidgetInstanceDto("quick-links",       "md", null),
        new DashboardWidgetInstanceDto("pending-approvals", "sm", null),
        new DashboardWidgetInstanceDto("exchange-rates",    "md", null),
        new DashboardWidgetInstanceDto("recent-documents",  "md", null),
        new DashboardWidgetInstanceDto("work-orders",       "md", null),
    };

    /// <summary>
    /// Kullanıcının saklı sayfa düzeni yokken kullanılacak varsayılan iki sayfa:
    /// "Genel" (standart widget'lar) + "Takvim" (takvim widget'ı).
    /// İzin filtresine GetConfigAsync içinde ayrıca tabi tutulur.
    /// </summary>
    public static IReadOnlyList<DashboardPageDto> DefaultPages() => new[]
    {
        new DashboardPageDto("page-genel", "Genel", DefaultLayout()),
        new DashboardPageDto("page-takvim", "Takvim", new DashboardWidgetInstanceDto[]
        {
            new DashboardWidgetInstanceDto("calendar", "lg", null, 3),
        }),
    };
}
