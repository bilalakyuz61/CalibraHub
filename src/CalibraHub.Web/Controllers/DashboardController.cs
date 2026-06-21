using System.Security.Claims;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Application.Services.Security;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class DashboardController : Controller
{
    private readonly IDbSchemaRepository     _dbSchema;
    private readonly IReportDesignRepository _designs;
    private readonly IPermissionService      _permService;
    private readonly PermissionDefDiscoveryService _permDiscovery;

    public DashboardController(
        IDbSchemaRepository dbSchema,
        IReportDesignRepository designs,
        IPermissionService permService,
        PermissionDefDiscoveryService permDiscovery)
    {
        _dbSchema      = dbSchema;
        _designs       = designs;
        _permService   = permService;
        _permDiscovery = permDiscovery;
    }

    // ── Rapor Panoları ────────────────────────────────────────────────────────────

    [HttpGet("Dashboard/Boards")]
    public async Task<IActionResult> Boards(CancellationToken ct)
    {
        if (!HasViewDashboardsPermission()) return Forbid();
        var config = await BuildBoardsConfigAsync(ct);
        ViewData["BoardConfigJson"] = JsonSerializer.Serialize(config);
        return View();
    }

    [HttpGet("Dashboard/BoardsConfig")]
    public async Task<IActionResult> BoardsConfig(CancellationToken ct)
    {
        if (!HasViewDashboardsPermission()) return Forbid();
        var config = await BuildBoardsConfigAsync(ct);
        return Json(config);
    }

    [HttpGet("Dashboard/View/{id:int}")]
    public async Task<IActionResult> View(int id, CancellationToken ct)
    {
        if (!HasViewDashboardsPermission()) return Forbid();
        if (!await CanAccessDashboardAsync(id, ct)) return Forbid();
        ViewData["Title"]    = "Rapor";
        ViewData["DesignId"] = id;
        return View();
    }

    // ── Rapor Tasarımları ─────────────────────────────────────────────────────────

    [HttpGet("Dashboard/Designer")]
    public async Task<IActionResult> Designer(CancellationToken ct)
    {
        if (!await CanAccessDesignerAsync(ct)) return Forbid();
        var config = await BuildDesignerBoardConfigAsync(ct);
        ViewData["BoardConfigJson"] = JsonSerializer.Serialize(config);
        return View();
    }

    [HttpGet("Dashboard/DesignerConfig")]
    public async Task<IActionResult> DesignerConfig(CancellationToken ct)
    {
        if (!await CanAccessDesignerAsync(ct)) return Forbid();
        var config = await BuildDesignerBoardConfigAsync(ct);
        return Json(config);
    }

    [HttpGet("Dashboard/DesignerEdit")]
    public async Task<IActionResult> DesignerEdit([FromQuery] int? load, CancellationToken ct)
    {
        if (!await CanAccessDesignerAsync(ct)) return Forbid();
        ViewData["Title"]  = "Rapor Tasarımcısı";
        ViewData["LoadId"] = load;
        return View();
    }

    // ── Tasarım CRUD ─────────────────────────────────────────────────────────────

    [HttpGet("Dashboard/DesignsList")]
    public async Task<IActionResult> DesignsList(CancellationToken ct)
    {
        if (!await CanAccessDesignerAsync(ct)) return Forbid();
        var list = await _designs.GetAllAsync(ct);
        return Json(list.Select(d => new
        {
            id        = d.Id,
            title     = d.Title,
            groupName = d.GroupName,
            created   = d.Created,
            createdBy = d.CreatedBy,
        }));
    }

    [HttpGet("Dashboard/LoadDesign/{id:int}")]
    public async Task<IActionResult> LoadDesign(int id, CancellationToken ct)
    {
        if (!await CanAccessDesignerAsync(ct)) return Forbid();
        var design = await _designs.GetByIdAsync(id, ct);
        if (design == null) return Json(new { ok = false, error = "Tasarım bulunamadı" });
        return Json(new { ok = true, title = design.Value.Title, groupName = design.Value.GroupName, description = design.Value.Description, panelsJson = design.Value.PanelsJson });
    }

    // Viewer için: yalnızca CanAccessDashboardAsync yeterliyken tasarım verisini döndürür.
    [HttpGet("Dashboard/LoadView/{id:int}")]
    public async Task<IActionResult> LoadView(int id, CancellationToken ct)
    {
        if (!HasViewDashboardsPermission() || !await CanAccessDashboardAsync(id, ct)) return Forbid();
        var design = await _designs.GetByIdAsync(id, ct);
        if (design == null) return Json(new { ok = false, error = "Rapor bulunamadı" });
        return Json(new { ok = true, title = design.Value.Title, panelsJson = design.Value.PanelsJson });
    }

    [HttpPost("Dashboard/SaveDesigned")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDesigned([FromBody] SaveDesignedRequest req, CancellationToken ct)
    {
        if (!await CanAccessDesignerAsync(ct))
            return Json(new { ok = false, error = "Yetersiz yetki" });
        if (string.IsNullOrWhiteSpace(req.Title))
            return Json(new { ok = false, error = "Rapor adı boş olamaz" });
        try
        {
            var panelsJson = JsonSerializer.Serialize(req.Panels);
            var saveReq    = new SaveReportDesignRequest(req.Title.Trim(), panelsJson, req.GroupName?.Trim(), req.Description?.Trim());
            if (req.Id.HasValue)
            {
                await _designs.UpdateAsync(req.Id.Value, saveReq, User.Identity?.Name, ct);
                await _permDiscovery.SyncDashboardPermissionAsync(req.Id.Value, req.Title.Trim(), true, ct);
                return Json(new { ok = true, id = req.Id.Value });
            }
            var newId = await _designs.SaveAsync(saveReq, User.Identity?.Name, ct);
            await _permDiscovery.SyncDashboardPermissionAsync(newId, req.Title.Trim(), true, ct);
            return Json(new { ok = true, id = newId });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    [HttpPost("Dashboard/DeleteDesign/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDesign(int id, CancellationToken ct)
    {
        if (!await CanAccessDesignerAsync(ct))
            return Json(new { ok = false, error = "Yetersiz yetki" });
        try
        {
            await _designs.DeleteAsync(id, ct);
            // Resource yetkisini pasif et — orphan UserPermission/DepartmentPermission
            // kayıtları korunur (silinen rapor geri eklenirse hemen tekrar geçerli olur).
            await _permDiscovery.SyncDashboardPermissionAsync(id, string.Empty, false, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── Veri kaynakları ──────────────────────────────────────────────────────────

    [HttpGet("Dashboard/DesignerSources")]
    public async Task<IActionResult> DesignerSources(CancellationToken ct)
    {
        if (!await CanAccessDesignerAsync(ct)) return Forbid();

        var views = await _dbSchema.GetDesignerViewsAsync(ct);
        var result = views
            .Where(v => !v.Name.StartsWith("cbv_Guide_", StringComparison.OrdinalIgnoreCase))
            .Select(v => new
            {
                name    = v.Name,
                label   = MakeViewLabel(v.Name),
                metrics = v.Columns
                    .Where(c => c.IsNumeric)
                    .Select(c => new { value = c.Name, label = c.Name, sqlType = c.SqlType })
                    .ToArray(),
                groups  = v.Columns
                    .Where(c => !c.IsNumeric)
                    .Select(c => new { value = c.Name, label = c.Name, isTime = c.IsTime, sqlType = c.SqlType })
                    .ToArray(),
            });

        return Json(result);
    }

    // ── Board config'ler ─────────────────────────────────────────────────────────

    private async Task<object> BuildBoardsConfigAsync(CancellationToken ct)
    {
        var list = await _designs.GetAllAsync(ct);
        // Kullanıcının yetkili olmadığı raporları liste'den çıkar.
        // Admin/SystemAdmin → hepsi; diğerleri → RESOURCE:DASH:{id} izni VEYA sahip (CreatedBy).
        var filtered = new List<ReportDesignSummaryDto>(list.Count);
        foreach (var d in list)
        {
            if (await CanAccessDashboardAsync(d.Id, d.CreatedBy, ct))
                filtered.Add(d);
        }
        var entities = filtered.Select(d => (object)new
        {
            id          = d.Id,
            title       = d.Title,
            subtitle    = string.IsNullOrWhiteSpace(d.GroupName) ? (d.CreatedBy ?? "—") : d.GroupName,
            description = (string?)null,
            imageUrl    = (string?)null,
            statusBadge = (object?)null,
            widgets     = new object[]
            {
                new { id = "w_date", type = "data", dataType = "text",
                      label = "Tarih", value = d.Created.ToLocalTime().ToString("dd MMM yyyy"),
                      detail = (string?)null, color = "slate" }
            },
            primaryAction = new
            {
                label      = "Görüntüle",
                icon       = "Eye",
                color      = "indigo",
                url        = $"/Dashboard/View/{d.Id}",
                hideButton = true,
            },
            secondaryAction = (object?)null,   // Rapor Panoları'nda düzenleme yok — yalnızca görüntüleme
        }).ToArray();

        return new
        {
            boardKey          = "dashboard-boards",
            title             = "Rapor Panoları",
            subtitle          = $"{entities.Length} rapor",
            icon              = "LayoutDashboard",
            iconColor         = "indigo",
            refreshUrl        = "/Dashboard/BoardsConfig",
            searchPlaceholder = "Rapor ara…",
            emptyText         = "Henüz kayıtlı rapor yok. Rapor Tasarımları ile oluşturun.",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Rapor", icon = "Plus", variant = "primary", url = "/Dashboard/DesignerEdit" }
            },
            masterWidgets = Array.Empty<object>(),
            entities,
        };
    }

    private async Task<object> BuildDesignerBoardConfigAsync(CancellationToken ct)
    {
        var list = await _designs.GetAllAsync(ct);
        var entities = list.Select(d => (object)new
        {
            id          = d.Id,
            title       = d.Title,
            subtitle    = string.IsNullOrWhiteSpace(d.GroupName) ? "Grupsuz" : d.GroupName,
            description = (string?)null,
            imageUrl    = (string?)null,
            statusBadge = (object?)null,
            widgets     = new object[]
            {
                new { id = "w_date", type = "data", dataType = "text",
                      label = "Oluşturulma", value = d.Created.ToLocalTime().ToString("dd MMM yyyy"),
                      detail = (string?)null, color = "slate" },
                new { id = "w_panels", type = "data", dataType = "text",
                      label = "Grup", value = string.IsNullOrWhiteSpace(d.GroupName) ? "—" : d.GroupName,
                      detail = (string?)null, color = "violet" },
            },
            primaryAction = new
            {
                label      = "Düzenle",
                icon       = "Edit2",
                color      = "violet",
                url        = $"/Dashboard/DesignerEdit?load={d.Id}",
                hideButton = true,
            },
            secondaryAction = new
            {
                label     = "Sil",
                icon      = "Trash2",
                apiUrl    = $"/Dashboard/DeleteDesign/{d.Id}",
                apiMethod = "POST",
                confirm   = $"'{d.Title}' tasarımını silmek istediğinize emin misiniz?",
            },
        }).ToArray();

        return new
        {
            boardKey          = "designer-designs",
            title             = "Rapor Tasarımları",
            subtitle          = $"{entities.Length} tasarım",
            icon              = "PenLine",
            iconColor         = "violet",
            refreshUrl        = "/Dashboard/DesignerConfig",
            searchPlaceholder = "Tasarım ara…",
            emptyText         = "Henüz kayıtlı tasarım yok. Yeni bir tasarım oluşturun.",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Tasarım", icon = "Plus", variant = "primary", url = "/Dashboard/DesignerEdit" }
            },
            masterWidgets = Array.Empty<object>(),
            entities,
        };
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────

    private static string MakeViewLabel(string name)
    {
        if (name.StartsWith("cbv_", StringComparison.OrdinalIgnoreCase)) return name[4..];
        if (name.StartsWith("vw_",  StringComparison.OrdinalIgnoreCase)) return name[3..];
        return name;
    }

    private bool HasViewDashboardsPermission()
    {
        var roleString = User.FindFirstValue(ClaimTypes.Role);
        if (!UserAuthorizationCatalog.TryParseRole(roleString, out var role)) return false;
        return UserAuthorizationCatalog.GetAllowedPermissions(role).Contains(UserPermission.ViewDashboards);
    }

    /// <summary>
    /// Rapor Tasarımcısı (REPORT_DESIGNER) erişim kontrolü:
    ///   - DepartmentManager / SystemAdmin → rol bazlı, her zaman true.
    ///   - Operator ve diğerleri → REPORT_DESIGNER:VIEW veya VIEW_OWN yetkisi gerekli.
    /// </summary>
    private async Task<bool> CanAccessDesignerAsync(CancellationToken ct)
    {
        var roleString = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (!UserAuthorizationCatalog.TryParseRole(roleString, out var role)) return false;

        // DM ve SA her zaman erişebilir (rol tabanlı).
        if (role is UserRole.DepartmentManager or UserRole.SystemAdmin)
            return true;

        // Diğerleri (Operator, Auditor, Approver): açık REPORT_DESIGNER yetkisi gerekli.
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) || uid <= 0)
            return false;
        int? deptId = int.TryParse(User.FindFirstValue("department_id"), out var d) && d > 0 ? d : null;
        return await _permService.CheckAnyAsync(uid, role, deptId, FormCodes.ReportDesigner,
            new[] { "VIEW", "VIEW_OWN" }, ct);
    }

    /// <summary>
    /// Rapor bazında erişim kontrolü. Mantık (öncelik sırasıyla):
    ///   1) SystemAdmin → her zaman true (bypass).
    ///   2) Dashboard sahibi (CreatedBy = current user) → true (kendi panosunu hep görür).
    ///   3) PermissionService.CheckAsync(RESOURCE:DASH:{id}) → true ise true.
    ///   4) Aksi → false.
    /// </summary>
    private async Task<bool> CanAccessDashboardAsync(int dashboardId, CancellationToken ct)
        => await CanAccessDashboardAsync(dashboardId, createdBy: null, ct);

    private async Task<bool> CanAccessDashboardAsync(int dashboardId, string? createdBy, CancellationToken ct)
    {
        var roleString = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        UserAuthorizationCatalog.TryParseRole(roleString, out var role);
        if (role == UserRole.SystemAdmin) return true;

        // Sahip kontrolü — kendisi yarattıysa hep görür (createdBy explicit verildiyse).
        var currentUserName = User.Identity?.Name ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(createdBy) &&
            string.Equals(createdBy.Trim(), currentUserName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // PermissionService — UserPermission > DepartmentPermission > default(false).
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return false;
        var deptStr = User.FindFirstValue("department_id");
        int? deptId = int.TryParse(deptStr, out var d) && d > 0 ? d : null;
        var actionCode = PermissionDefDiscoveryService.BuildDashboardActionCode(dashboardId);
        return await _permService.CheckAsync(userId, role, deptId, FormCodes.Dashboards, actionCode, ct);
    }
}

public sealed class SaveDesignedRequest
{
    public int?      Id          { get; set; }
    public string    Title       { get; set; } = string.Empty;
    public string?   GroupName   { get; set; }
    public string?   Description { get; set; }
    public object[]? Panels      { get; set; }
}
