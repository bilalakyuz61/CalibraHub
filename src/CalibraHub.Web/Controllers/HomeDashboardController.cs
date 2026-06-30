using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Infrastructure.Ui;
using CalibraHub.Web.Models.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// 2026-06-14 — Ana Sayfa Panosu (Home Dashboard) endpoint'leri.
///
/// React panosu (Shell'in EmptyState slotuna gömülü) tüm verisini buradan
/// <c>fetch</c> ile alır. Layout + katalog + quick-link seçenekleri tek
/// <c>Config</c> çağrısıyla; her widget verisi ayrı (lazy) endpoint'le gelir.
///
/// Rota kasıtlı olarak <c>/HomeDashboard</c> — <c>/Dashboard</c> rapor tasarımcısı
/// tarafından kullanıldığı için çakışmayı önlemek amacıyla ayrı prefix.
///
/// Yetki: her veri endpoint'i kendi FormCode'unu yeniden kontrol eder
/// (DashboardService.CanSeeWidgetAsync) — forged client isteği katalogun
/// gizlediği veriyi çekemez. <c>Config</c> herkese açıktır; gövdesi zaten
/// izin-filtrelidir.
/// </summary>
[Authorize]
[Route("HomeDashboard/[action]")]
public sealed class HomeDashboardController : Controller
{
    private readonly IDashboardService _dashboard;
    private readonly IPermissionService _permissions;
    private readonly IMenuService _menuService;
    private readonly IUiTextService _uiText;

    public HomeDashboardController(
        IDashboardService dashboard,
        IPermissionService permissions,
        IMenuService menuService,
        IUiTextService uiText)
    {
        _dashboard = dashboard;
        _permissions = permissions;
        _menuService = menuService;
        _uiText = uiText;
    }

    // ════════════════════════════════════════════════════════════════
    // Config + layout
    // ════════════════════════════════════════════════════════════════

    /// <summary>GET /HomeDashboard/Config — layout + katalog + quick-link seçenekleri (hepsi izin-filtreli).</summary>
    [HttpGet]
    public async Task<IActionResult> Config(CancellationToken ct)
    {
        var (userId, role, deptId) = GetCurrentUser();
        if (userId <= 0) return Unauthorized(new { ok = false, error = "Oturum açık değil." });

        var config = await _dashboard.GetConfigAsync(userId, role, deptId, ct);

        // Quick-link seçenekleri Web katmanında üretilir (menü URL'leri MenuDefinition'da).
        var quickLinks = await BuildQuickLinkOptionsAsync(userId, role, deptId, ct);
        var enriched = config with { QuickLinkOptions = quickLinks };

        return Json(new { ok = true, config = enriched });
    }

    /// <summary>POST /HomeDashboard/SavePages — kullanıcı sayfa düzenini JSON olarak kalıcılaştır.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePages([FromBody] SaveDashboardPagesRequest request, CancellationToken ct)
    {
        var (userId, _, _) = GetCurrentUser();
        if (userId <= 0) return Unauthorized(new { ok = false, error = "Oturum açık değil." });
        if (request is null) return Json(new { ok = false, error = "Geçersiz istek." });

        try
        {
            await _dashboard.SavePagesAsync(userId, request, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>POST /HomeDashboard/ResetPages — saklı düzeni sil, varsayılan sayfaları döndür.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPages(CancellationToken ct)
    {
        var (userId, role, deptId) = GetCurrentUser();
        if (userId <= 0) return Unauthorized(new { ok = false, error = "Oturum açık değil." });

        var config = await _dashboard.ResetLayoutAsync(userId, role, deptId, ct);
        var quickLinks = await BuildQuickLinkOptionsAsync(userId, role, deptId, ct);
        var enriched = config with { QuickLinkOptions = quickLinks };

        return Json(new { ok = true, config = enriched });
    }

    // ════════════════════════════════════════════════════════════════
    // Widget veri endpoint'leri (her biri kendi izin kapısını yeniden kontrol eder)
    // ════════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> PendingApprovals(CancellationToken ct)
    {
        if (!await CanSeeAsync("pending-approvals", ct)) return Forbid403();
        var data = await _dashboard.GetPendingApprovalsAsync(ct);
        return Json(new { ok = true, data });
    }

    [HttpGet]
    public async Task<IActionResult> ExchangeRates([FromQuery] string? codes, CancellationToken ct)
    {
        if (!await CanSeeAsync("exchange-rates", ct)) return Forbid403();
        var codeList = (codes ?? "USD,EUR,GBP")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var items = await _dashboard.GetExchangeRatesAsync(codeList, ct);
        return Json(new { ok = true, items });
    }

    [HttpGet]
    public async Task<IActionResult> RecentDocuments([FromQuery] int take, CancellationToken ct)
    {
        if (!await CanSeeAsync("recent-documents", ct)) return Forbid403();
        var items = await _dashboard.GetRecentDocumentsAsync(take <= 0 ? 8 : take, ct);
        return Json(new { ok = true, items });
    }

    [HttpGet]
    public async Task<IActionResult> WorkOrderSummary(CancellationToken ct)
    {
        if (!await CanSeeAsync("work-orders", ct)) return Forbid403();
        var data = await _dashboard.GetWorkOrderSummaryAsync(ct);
        return Json(new { ok = true, data });
    }

    [HttpGet]
    public async Task<IActionResult> SalesQuoteSummary(CancellationToken ct)
    {
        if (!await CanSeeAsync("sales-quotes", ct)) return Forbid403();
        var data = await _dashboard.GetSalesQuoteSummaryAsync(ct);
        return Json(new { ok = true, data });
    }

    [HttpGet]
    public async Task<IActionResult> StockAlerts([FromQuery] int take, CancellationToken ct)
    {
        if (!await CanSeeAsync("stock-alerts", ct)) return Forbid403();
        var items = await _dashboard.GetStockAlertsAsync(take <= 0 ? 8 : take, ct);
        // items boşsa client "yapılandırılmadı / uyarı yok" boş durumu gösterir.
        return Json(new { ok = true, items, configured = items.Count > 0 });
    }

    // ════════════════════════════════════════════════════════════════
    // Quick-link seçenekleri — yetkili menü yaprakları (url != null)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// MenuDefinition ağacını (DB-driven varsa enjekte ederek) kurar, izinle filtreler,
    /// sonra URL'i olan yaprakları QuickLinkOptionDto olarak düzleştirir. Böylece kullanıcı
    /// yalnızca açabildiği ekranlara kısayol iğneleyebilir.
    /// </summary>
    private async Task<IReadOnlyList<QuickLinkOptionDto>> BuildQuickLinkOptionsAsync(
        int userId, UserRole role, int? deptId, CancellationToken ct)
    {
        var runtime = await _uiText.GetRuntimeAsync(ct);
        var lang = runtime.LanguageCode;
        var isEn = string.Equals(lang, "en-US", StringComparison.OrdinalIgnoreCase);
        var isSystemAdmin = role == UserRole.SystemAdmin;

        // Statik ağaç + (varsa) DB enjeksiyonu — _Layout.cshtml ile aynı dual-track.
        IReadOnlyList<MenuDefinition.MenuNode> full;
#pragma warning disable CS0618 // GetMainMenu fallback — DB verisi yoksa kasıtlı.
        var staticBase = MenuDefinition.GetMainMenu(isSystemAdmin, lang);
#pragma warning restore CS0618
        if (await _menuService.HasMenuDataAsync(ct))
        {
            var dbItems = await _menuService.GetMenuItemsAsync(ct);
            full = MenuDefinition.InjectDbItems(staticBase, dbItems, isEn);
        }
        else
        {
            full = staticBase;
        }

        // İzin filtresi — menüyle birebir aynı görünürlük.
        var filtered = await MenuDefinition.FilterByPermissionAsync(full, _permissions, userId, role, deptId, ct);

        var options = new List<QuickLinkOptionDto>();
        FlattenLeaves(filtered, parentLabel: null, options);
        return options;
    }

    /// <summary>Ağacı gez, URL'i olan (tıklanabilir) yaprakları topla.</summary>
    private static void FlattenLeaves(
        IReadOnlyList<MenuDefinition.MenuNode> nodes,
        string? parentLabel,
        List<QuickLinkOptionDto> sink)
    {
        foreach (var node in nodes)
        {
            var hasChildren = node.Children is { Count: > 0 };
            if (hasChildren)
            {
                // Grup başlığı → çocuklara in. Grup etiketini taşı (en yakın anlamlı grup).
                FlattenLeaves(node.Children!, node.Label, sink);
            }
            else if (!string.IsNullOrEmpty(node.Url))
            {
                sink.Add(new QuickLinkOptionDto(
                    Key: node.Key,
                    Label: node.Label,
                    Url: node.Url!,
                    Icon: node.Icon,
                    GroupLabel: parentLabel ?? node.Label));
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Yardımcılar
    // ════════════════════════════════════════════════════════════════

    private async Task<bool> CanSeeAsync(string widgetType, CancellationToken ct)
    {
        var (userId, role, deptId) = GetCurrentUser();
        if (userId <= 0) return false;
        return await _dashboard.CanSeeWidgetAsync(userId, role, deptId, widgetType, ct);
    }

    private IActionResult Forbid403() => Json(new { ok = false, error = "forbidden" });

    /// <summary>PermissionController.GetCurrentUser() ile aynı — claim tuple çıkarımı.</summary>
    private (int UserId, UserRole Role, int? DepartmentId) GetCurrentUser()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var roleStr = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var deptStr = User.FindFirstValue("department_id");
        int.TryParse(userIdStr, out var userId);
        int? deptId = int.TryParse(deptStr, out var d) && d > 0 ? d : null;
        return (userId, ParseRole(roleStr), deptId);
    }

    private static UserRole ParseRole(string role) =>
        CalibraHub.Application.Security.UserAuthorizationCatalog.TryParseRole(role, out var r)
            ? r : UserRole.Operator;
}
