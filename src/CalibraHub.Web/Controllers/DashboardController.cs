using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Configuration;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class DashboardController : Controller
{
    private readonly GrafanaOptions             _grafanaOptions;
    private readonly IGrafanaProvisioningService _grafanaProvisioning;
    private readonly IReportDashboardService    _reportDashboards;
    private readonly IUserProfileRepository     _userRepo;
    private readonly IDepartmentRepository      _deptRepo;

    public DashboardController(
        IOptions<GrafanaOptions>      grafanaOptions,
        IGrafanaProvisioningService   grafanaProvisioning,
        IReportDashboardService       reportDashboards,
        IUserProfileRepository        userRepo,
        IDepartmentRepository         deptRepo)
    {
        _grafanaOptions      = grafanaOptions.Value;
        _grafanaProvisioning = grafanaProvisioning;
        _reportDashboards    = reportDashboards;
        _userRepo            = userRepo;
        _deptRepo            = deptRepo;
    }

    // Pano master-detail sayfasi: solda pano listesi, sagda secili pano kiosk iframe.
    [HttpGet]
    public IActionResult Index()
    {
        if (!_grafanaOptions.Enabled)
        {
            ViewData["Title"] = "Rapor Panoları";
            return View("Disabled");
        }

        if (!HasViewDashboardsPermission()) return Forbid();

        ViewData["Title"]             = "Rapor Panoları";
        ViewData["GrafanaPublicPath"] = _grafanaOptions.PublicPath;
        ViewData["GrafanaIsDesigner"] = HasDesignDashboardsPermission();
        return View();
    }

    // Grafana arayuzu kisayolu: tam Grafana UI (kendi menusu, dashboard yonetimi vb.).
    [HttpGet("Dashboard/Grafana")]
    public IActionResult Grafana()
    {
        if (!_grafanaOptions.Enabled)
        {
            ViewData["Title"] = "Rapor Tasarımı";
            return View("Disabled");
        }

        if (!HasViewDashboardsPermission()) return Forbid();

        ViewData["Title"]             = "Rapor Tasarımı";
        ViewData["GrafanaPublicPath"] = _grafanaOptions.PublicPath;
        ViewData["GrafanaIsDesigner"] = HasDesignDashboardsPermission();
        return View();
    }

    // Pano listesi — per-dashboard erişim filtresi uygulanır.
    [HttpGet("Dashboard/List")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!_grafanaOptions.Enabled) return Json(Array.Empty<object>());
        if (!HasViewDashboardsPermission()) return Forbid();

        var orgIdRaw = User.FindFirstValue("grafana_org_id");
        if (!int.TryParse(orgIdRaw, out var orgId) || orgId <= 0)
            return Json(Array.Empty<object>());

        var allDashboards = await _grafanaProvisioning.ListDashboardsAsync(orgId, ct);

        var userIdRaw  = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var deptIdRaw  = User.FindFirstValue("department_id");
        var roleString = User.FindFirstValue(ClaimTypes.Role);

        if (!int.TryParse(userIdRaw, out var userId) ||
            !UserAuthorizationCatalog.TryParseRole(roleString, out var role))
            return Json(Array.Empty<object>());

        int? departmentId = int.TryParse(deptIdRaw, out var dId) ? dId : null;

        var filtered = await _reportDashboards.GetAccessibleAsync(
            allDashboards, userId, departmentId, role, User.Identity?.Name, ct);
        return Json(filtered);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Admin: Pano erişim yönetimi
    // ──────────────────────────────────────────────────────────────────────────

    [HttpGet("Dashboard/AccessConfig")]
    public IActionResult AccessConfig()
    {
        if (!HasDesignDashboardsPermission()) return Forbid();
        ViewData["Title"] = "Pano Erişim Yönetimi";
        return View();
    }

    // Tüm panoları erişim ayarları + kullanıcı/departman lookup'larıyla döner.
    [HttpGet("Dashboard/GetAllDashboardAccess")]
    public async Task<IActionResult> GetAllDashboardAccess(CancellationToken ct)
    {
        if (!HasDesignDashboardsPermission()) return Forbid();

        IReadOnlyList<GrafanaDashboardSummary> grafanaDashboards = [];
        var orgIdRaw = User.FindFirstValue("grafana_org_id");
        if (_grafanaOptions.Enabled && int.TryParse(orgIdRaw, out var orgId) && orgId > 0)
            grafanaDashboards = await _grafanaProvisioning.ListDashboardsAsync(orgId, ct);

        var dashboards  = await _reportDashboards.GetAllWithAccessAsync(grafanaDashboards, User.Identity?.Name, ct);
        var users       = await _userRepo.GetAllAsync(ct);
        var departments = await _deptRepo.GetAllAsync(ct);

        return Json(new
        {
            dashboards,
            users = users
                .Where(u => u.IsActive)
                .OrderBy(u => u.FullName)
                .Select(u => new { u.Id, u.FullName }),
            departments = departments
                .Where(d => d.IsActive)
                .OrderBy(d => d.Name)
                .Select(d => new { d.Id, d.Name }),
        });
    }

    [HttpPost("Dashboard/SetDashboardAccess")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDashboardAccess([FromBody] SetDashboardAccessRequest req, CancellationToken ct)
    {
        if (!HasDesignDashboardsPermission())
            return Json(new { ok = false, error = "Yetersiz yetki" });
        try
        {
            await _reportDashboards.SetAccessAsync(
                req.GrafanaUid, req.UserIds, req.DepartmentIds, User.Identity?.Name, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    private bool HasViewDashboardsPermission()
    {
        var roleString = User.FindFirstValue(ClaimTypes.Role);
        if (!UserAuthorizationCatalog.TryParseRole(roleString, out var role)) return false;
        return UserAuthorizationCatalog.GetAllowedPermissions(role).Contains(UserPermission.ViewDashboards);
    }

    // Grafana sol menusu/edit araclari icin DesignDashboards yetki kontrolu.
    // Yetkisi olanlar (SystemAdmin, DepartmentManager) Grafana UI'sini tam gorur;
    // Viewer-tipi roller (Approver, Operator, Auditor) kiosk modunda izler.
    private bool HasDesignDashboardsPermission()
    {
        var roleString = User.FindFirstValue(ClaimTypes.Role);
        if (!UserAuthorizationCatalog.TryParseRole(roleString, out var role)) return false;
        return UserAuthorizationCatalog.GetAllowedPermissions(role).Contains(UserPermission.DesignDashboards);
    }
}

public sealed class SetDashboardAccessRequest
{
    public string              GrafanaUid    { get; set; } = string.Empty;
    public IReadOnlyList<int>  UserIds       { get; set; } = [];
    public IReadOnlyList<int>  DepartmentIds { get; set; } = [];
}
