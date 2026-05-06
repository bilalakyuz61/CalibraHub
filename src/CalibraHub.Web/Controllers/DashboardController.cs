using System.Security.Claims;
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
    private readonly GrafanaOptions _grafanaOptions;
    private readonly IGrafanaProvisioningService _grafanaProvisioning;

    public DashboardController(
        IOptions<GrafanaOptions> grafanaOptions,
        IGrafanaProvisioningService grafanaProvisioning)
    {
        _grafanaOptions = grafanaOptions.Value;
        _grafanaProvisioning = grafanaProvisioning;
    }

    // Pano master-detail sayfasi: solda pano listesi, sagda secili pano kiosk iframe.
    [HttpGet]
    public IActionResult Index()
    {
        if (!_grafanaOptions.Enabled)
        {
            ViewData["Title"] = "Panolar";
            return View("Disabled");
        }

        if (!HasViewDashboardsPermission()) return Forbid();

        ViewData["Title"] = "Panolar";
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
            ViewData["Title"] = "Grafana";
            return View("Disabled");
        }

        if (!HasViewDashboardsPermission()) return Forbid();

        ViewData["Title"] = "Grafana";
        ViewData["GrafanaPublicPath"] = _grafanaOptions.PublicPath;
        ViewData["GrafanaIsDesigner"] = HasDesignDashboardsPermission();
        return View();
    }

    [HttpGet("Dashboard/List")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!_grafanaOptions.Enabled)
        {
            return Json(Array.Empty<object>());
        }

        if (!HasViewDashboardsPermission()) return Forbid();

        var orgIdRaw = User.FindFirstValue("grafana_org_id");
        if (!int.TryParse(orgIdRaw, out var orgId) || orgId <= 0)
        {
            return Json(Array.Empty<object>());
        }

        var items = await _grafanaProvisioning.ListDashboardsAsync(orgId, ct);
        return Json(items);
    }

    private bool HasViewDashboardsPermission()
    {
        var roleString = User.FindFirstValue(ClaimTypes.Role);
        if (!UserAuthorizationCatalog.TryParseRole(roleString, out var role)) return false;

        var permissions = UserAuthorizationCatalog.GetAllowedPermissions(role);
        return permissions.Contains(UserPermission.ViewDashboards);
    }

    // Grafana sol menusu/edit araclari icin DesignDashboards yetki kontrolu.
    // Yetkisi olanlar (SystemAdmin, DepartmentManager) Grafana UI'sini tam gorur;
    // Viewer-tipi roller (Approver, Operator, Auditor) kiosk modunda izler.
    private bool HasDesignDashboardsPermission()
    {
        var roleString = User.FindFirstValue(ClaimTypes.Role);
        if (!UserAuthorizationCatalog.TryParseRole(roleString, out var role)) return false;

        var permissions = UserAuthorizationCatalog.GetAllowedPermissions(role);
        return permissions.Contains(UserPermission.DesignDashboards);
    }
}
