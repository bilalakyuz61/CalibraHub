using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// 2026-06-06 — Yetkilendirme yönetimi endpoint'leri.
///
/// /Permission/My                        → mevcut kullanıcının efektif izin matrisi (client gate'leri için)
/// /Permission/Department/{id}           → admin için departman matrisi
/// /Permission/Department/{id}/Save POST → matrisi toplu replace
/// /Permission/User/{id}                 → admin için kullanıcı override matrisi
/// /Permission/User/{id}/Save POST       → matrisi toplu replace
/// </summary>
[Authorize]
[Route("Permission")]
public sealed class PermissionController : Controller
{
    private readonly IPermissionService _permService;
    private readonly IPermissionGrantRepository _grantRepo;
    private readonly IUserProfileRepository _userRepo;

    public PermissionController(
        IPermissionService permService,
        IPermissionGrantRepository grantRepo,
        IUserProfileRepository userRepo)
    {
        _permService = permService;
        _grantRepo = grantRepo;
        _userRepo = userRepo;
    }

    // ── Current user — client-side gate'leri için ─────────────────────────
    [HttpGet("My")]
    public async Task<IActionResult> My(CancellationToken ct)
    {
        var (userId, role, deptId) = GetCurrentUser();
        if (userId <= 0) return Unauthorized();
        var effective = await _permService.GetEffectivePermissionsAsync(userId, role, deptId, ct);
        return Json(new { ok = true, permissions = effective });
    }

    // ── Departman matrix ──────────────────────────────────────────────────
    [HttpGet("Department/{id:int}")]
    public async Task<IActionResult> DepartmentMatrix(int id, CancellationToken ct)
    {
        if (!await CanManagePermissionsAsync(ct)) return StatusCode(403, new { ok = false, error = "Yetki yönetimi için yetkiniz yok." });
        var matrix = await _permService.GetDepartmentPermissionsAsync(id, ct);
        return Json(new { ok = true, departmentId = id, permissions = matrix });
    }

    [HttpPost("Department/{id:int}/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DepartmentSave(
        int id, [FromBody] BulkAssignPermissionRequest request, CancellationToken ct)
    {
        if (!await CanManagePermissionsAsync(ct)) return StatusCode(403, new { ok = false, error = "Yetki yönetimi için yetkiniz yok." });
        if (request is null || request.Items is null)
            return Json(new { ok = false, error = "Geçersiz istek." });

        var (_, _, _) = GetCurrentUser();
        var grants = request.Items.Select(i => new PermissionGrant
        {
            DepartmentId    = id,
            PermissionDefId = i.PermissionDefId,
            IsGranted       = i.IsGranted,
            CreatedById     = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var _duid) ? _duid : (int?)null,
        }).ToList();

        await _grantRepo.BulkReplaceForOwnerAsync(userId: null, departmentId: id, grants, ct);
        _permService.InvalidateCache(departmentId: id);
        return Json(new { ok = true, count = grants.Count });
    }

    // ── Kullanıcı matrix ──────────────────────────────────────────────────
    [HttpGet("User/{id:int}")]
    public async Task<IActionResult> UserMatrix(int id, CancellationToken ct)
    {
        if (!await CanManagePermissionsAsync(ct)) return StatusCode(403, new { ok = false, error = "Yetki yönetimi için yetkiniz yok." });
        var target = await _userRepo.GetByIdAsync(id, ct);
        if (target is null) return NotFound(new { ok = false, error = "Kullanıcı bulunamadı." });
        var effective = await _permService.GetEffectivePermissionsAsync(id, target.Role, target.DepartmentId, ct);
        return Json(new { ok = true, userId = id, departmentId = target.DepartmentId, role = target.Role.ToString(), permissions = effective });
    }

    [HttpPost("User/{id:int}/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UserSave(
        int id, [FromBody] BulkAssignPermissionRequest request, CancellationToken ct)
    {
        if (!await CanManagePermissionsAsync(ct)) return StatusCode(403, new { ok = false, error = "Yetki yönetimi için yetkiniz yok." });
        if (request is null || request.Items is null)
            return Json(new { ok = false, error = "Geçersiz istek." });

        var target = await _userRepo.GetByIdAsync(id, ct);
        if (target is null) return NotFound(new { ok = false, error = "Kullanıcı bulunamadı." });

        var grants = request.Items.Select(i => new PermissionGrant
        {
            UserId          = id,
            PermissionDefId = i.PermissionDefId,
            IsGranted       = i.IsGranted,
            CreatedById     = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var _uuid) ? _uuid : (int?)null,
        }).ToList();

        await _grantRepo.BulkReplaceForOwnerAsync(userId: id, departmentId: null, grants, ct);
        // Hem null-dept key'i hem de kullanıcının gerçek dept'iyle olan key'i temizle.
        _permService.InvalidateCache(userId: id, departmentId: target.DepartmentId);
        return Json(new { ok = true, count = grants.Count });
    }

    // ── Tanılama (geçici) ─────────────────────────────────────────────────

    /// <summary>
    /// Mevcut giriş yapmış kullanıcının kendi yetki durumunu görmesini sağlar.
    /// GET /Permission/MySelf
    /// Bilal gibi bir kullanıcı bu URL'e gidince JWT'den gelen userId/role/deptId
    /// ve DB'deki grant'ları görebilir.
    /// </summary>
    [HttpGet("MySelf")]
    public async Task<IActionResult> MySelf(CancellationToken ct)
    {
        var (userId, role, deptId) = GetCurrentUser();
        if (userId <= 0) return Unauthorized(new { ok = false, error = "Oturum açık değil." });

        // JWT claim'leri
        var userIdClaim   = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var roleClaim     = User.FindFirstValue(ClaimTypes.Role);
        var deptClaim     = User.FindFirstValue("department_id");
        var emailClaim    = User.FindFirstValue(ClaimTypes.Email);
        var companyIdClaim= User.FindFirstValue("company_id");

        // Ham grant'lar
        var rawGrants = await _grantRepo.ListByUserAsync(userId, ct);

        // Efektif izinler
        var effective = await _permService.GetEffectivePermissionsAsync(userId, role, deptId, ct);
        var allowedList = effective
            .Where(e => e.IsAllowed)
            .Select(e => new { e.FormCode, e.ActionCode, e.Source })
            .ToList();

        // Menü düğümlerinin filtre durumu (hangi formCode'ların izinli/izinsiz olduğunu kontrol et)
        var permFormCodes = new[]
        {
            "NOTES","MATERIAL_CARD_EDIT","PRODUCT_CONFIG","SALES_QUOTE","SALES_ORDER",
            "BOM_EDIT","CONTACTS","LOCATIONS","MEASURE_UNITS","CARD_GROUPS","PRICE_LIST",
            "SETUP_DEFINITIONS","PERMISSION_MGMT"
        };
        var menuCheckResults = new List<object>();
        foreach (var fc in permFormCodes)
        {
            var allowed = await _permService.CheckAnyAsync(userId, role, deptId, fc,
                new[] { "VIEW","CREATE","EDIT_OWN","EDIT_ALL","DELETE_OWN","DELETE_ALL" }, ct);
            menuCheckResults.Add(new { formCode = fc, wouldShowInMenu = allowed });
        }

        return Json(new
        {
            ok = true,
            claims = new { userIdClaim, roleClaim, deptClaim, emailClaim, companyIdClaim },
            parsedUserId      = userId,
            parsedRole        = role.ToString(),
            parsedDepartmentId= deptId,
            rawGrantCount     = rawGrants.Count,
            rawGrants         = rawGrants.Select(g => new { g.Id, g.PermissionDefId, g.IsGranted, g.UserId, g.DepartmentId }),
            allowedCount      = allowedList.Count,
            allowed           = allowedList,
            menuFormCodeCheck = menuCheckResults,
        });
    }

    /// <summary>
    /// Geçici tanılama endpoint'i — admin bir kullanıcının DB grant'larını ve
    /// etkin menü öğelerini görebilir. Prod'a çıkmadan önce kaldır.
    /// GET /Permission/Diag?userId=X
    /// </summary>
    [HttpGet("Diag")]
    public async Task<IActionResult> Diag(int userId, CancellationToken ct)
    {
        if (!await CanManagePermissionsAsync(ct))
            return StatusCode(403, new { ok = false, error = "Yetki gerekli." });

        var target = await _userRepo.GetByIdAsync(userId, ct);
        if (target is null)
            return NotFound(new { ok = false, error = "Kullanıcı bulunamadı." });

        // Ham grant'lar (cache atla)
        var rawGrants = await _grantRepo.ListByUserAsync(userId, ct);

        // Efektif izinler (cache'li)
        var effective = await _permService.GetEffectivePermissionsAsync(userId, target.Role, target.DepartmentId, ct);
        var allowedList = effective
            .Where(e => e.IsAllowed)
            .Select(e => new { e.FormCode, e.ActionCode, e.Source })
            .ToList();

        return Json(new
        {
            ok          = true,
            userId,
            role        = target.Role.ToString(),
            departmentId= target.DepartmentId,
            rawGrantCount = rawGrants.Count,
            rawGrants   = rawGrants.Select(g => new
            {
                g.Id, g.PermissionDefId, g.IsGranted, g.UserId, g.DepartmentId
            }),
            allowedCount = allowedList.Count,
            allowed      = allowedList,
        });
    }

    // ── Yardımcılar ───────────────────────────────────────────────────────
    private (int UserId, UserRole Role, int? DepartmentId) GetCurrentUser()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var roleStr = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var deptStr = User.FindFirstValue("department_id");
        int.TryParse(userIdStr, out var userId);
        int? deptId = int.TryParse(deptStr, out var d) && d > 0 ? d : null;
        return (userId, ParseRole(roleStr), deptId);
    }

    /// <summary>
    /// Role claim hem enum adi ("SystemAdmin") hem Turkce label ("Sistem Admin") olabilir.
    /// UserAuthorizationCatalog.TryParseRole her ikisini de destekler.
    /// </summary>
    private static UserRole ParseRole(string role) =>
        CalibraHub.Application.Security.UserAuthorizationCatalog.TryParseRole(role, out var r)
            ? r : UserRole.Operator;

    private async Task<bool> CanManagePermissionsAsync(CancellationToken ct)
    {
        var (userId, role, deptId) = GetCurrentUser();
        if (userId <= 0) return false;
        if (role == UserRole.SystemAdmin || role == UserRole.DepartmentManager) return true;
        return await _permService.CheckAnyAsync(userId, role, deptId,
            "PERMISSION_MGMT", new[] { PermissionDef.StandardActions.View }, ct);
    }
}
