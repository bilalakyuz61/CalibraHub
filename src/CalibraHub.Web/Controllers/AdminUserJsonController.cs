using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// AdminUserJsonController — Kullanici tanimi JSON CRUD endpoint'leri
/// (rapor §2.3 AdminController split).
///
/// Tasinmis endpoint'ler:
///   - GET  /Admin/GetAdminUsersJson      → liste (search + companyId)
///   - GET  /Admin/GetUsersFormDataJson   → dropdown'lar (sirket/dep/sup/role/perm)
///   - POST /Admin/SaveAdminUserJson      → yeni kullanici
///   - POST /Admin/UpdateAdminUserJson    → guncelle
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.SetupDefinitions)]
public sealed class AdminUserJsonController : Controller
{
    private readonly IAdminReadService _adminReadService;
    private readonly IAdminManagementService _adminManagementService;

    public AdminUserJsonController(
        IAdminReadService adminReadService,
        IAdminManagementService adminManagementService)
    {
        _adminReadService = adminReadService;
        _adminManagementService = adminManagementService;
    }

    [HttpGet("/Admin/GetAdminUsersJson")]
    public async Task<IActionResult> GetAdminUsersJson(string? search, int? companyId, CancellationToken cancellationToken)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var items = snapshot.Users.AsEnumerable();
        if (companyId.HasValue)
            items = items.Where(x => x.CompanyId == companyId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim().ToLowerInvariant();
            items = items.Where(x =>
                (x.FullName ?? "").ToLowerInvariant().Contains(q) ||
                (x.Email ?? "").ToLowerInvariant().Contains(q) ||
                (x.EmployeeCode ?? "").ToLowerInvariant().Contains(q));
        }
        return Json(items.Select(x => new
        {
            x.Id, x.CompanyId, x.CompanyName, x.FullName, x.Email, x.EmployeeCode,
            x.DepartmentName, supervisorName = x.SupervisorName ?? "-",
            x.Role, permissions = string.Join(", ", x.Permissions),
            x.IsActive
        }).ToArray());
    }

    [HttpGet("/Admin/GetUsersFormDataJson")]
    public async Task<IActionResult> GetUsersFormDataJson(int? companyId, CancellationToken cancellationToken)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);

        var companies = snapshot.Companies
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { id = x.Id.ToString(), name = x.Name })
            .ToArray();

        var resolvedCompanyId = companyId ?? (companies.Length > 0 ? int.Parse(companies[0].id) : (int?)null);

        var departments = snapshot.Departments
            .Where(x => !resolvedCompanyId.HasValue || x.CompanyId == resolvedCompanyId.Value)
            .Select(x => new { id = x.Id.ToString(), name = x.Name })
            .ToArray();

        var supervisors = snapshot.Users
            .Where(x => !resolvedCompanyId.HasValue || x.CompanyId == resolvedCompanyId.Value)
            .Select(x => new { id = x.Id.ToString(), name = x.FullName })
            .ToArray();

        var roles = UserAuthorizationCatalog.Roles
            .Select(r => new { value = r.ToString(), label = UserAuthorizationCatalog.GetRoleLabel(r) })
            .ToArray();

        var permissions = UserAuthorizationCatalog.Permissions
            .Select(p => new { value = p.ToString(), label = UserAuthorizationCatalog.GetPermissionLabel(p) })
            .ToArray();

        return Json(new { companies, departments, supervisors, roles, permissions });
    }

    [HttpPost("/Admin/SaveAdminUserJson")]
    public async Task<IActionResult> SaveAdminUserJson([FromBody] UserCreateInput input, CancellationToken cancellationToken)
    {
        if (!input.CompanyId.HasValue)
            return Json(new { success = false, message = "Sirket secimi zorunludur." });
        if (!input.DepartmentId.HasValue)
            return Json(new { success = false, message = "Departman secimi zorunludur." });
        if (string.IsNullOrWhiteSpace(input.FullName))
            return Json(new { success = false, message = "Ad Soyad zorunludur." });
        if (string.IsNullOrWhiteSpace(input.Email))
            return Json(new { success = false, message = "E-posta zorunludur." });
        if (string.IsNullOrWhiteSpace(input.EmployeeCode))
            return Json(new { success = false, message = "Sicil kodu zorunludur." });
        if (string.IsNullOrWhiteSpace(input.Role) || !TryParseRole(input.Role, out var role))
            return Json(new { success = false, message = "Gecerli bir rol seciniz." });

        input.Permissions ??= new List<string>();
        input.Permissions = input.Permissions.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!TryParsePermissions(input.Permissions, out var permissions))
            return Json(new { success = false, message = "Secilen yetkilerden biri gecersiz." });

        try
        {
            await _adminManagementService.CreateUserAsync(
                new CreateUserRequest(
                    input.CompanyId.Value,
                    input.FullName,
                    input.Email,
                    input.EmployeeCode,
                    input.DepartmentId.Value,
                    input.SupervisorUserId,
                    role,
                    permissions,
                    Password: null),
                cancellationToken);
            return Json(new { success = true, message = "Kullanici olusturuldu." });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    /// <summary>Mevcut kullanicinin temel bilgilerini gunceller.</summary>
    [HttpPost("/Admin/UpdateAdminUserJson")]
    public async Task<IActionResult> UpdateAdminUserJson(
        [FromBody] UserUpdateInput input, CancellationToken cancellationToken)
    {
        if (input.Id <= 0)
            return Json(new { success = false, message = "Kullanici Id zorunlu." });
        if (!input.CompanyId.HasValue)
            return Json(new { success = false, message = "Sirket secimi zorunludur." });
        if (string.IsNullOrWhiteSpace(input.FullName))
            return Json(new { success = false, message = "Ad Soyad zorunludur." });
        if (string.IsNullOrWhiteSpace(input.Email))
            return Json(new { success = false, message = "E-posta zorunludur." });

        try
        {
            await _adminManagementService.UpdateUserAsync(
                new UpdateUserRequest(
                    input.Id,
                    input.CompanyId.Value,
                    input.FullName,
                    input.Email,
                    Password: null),
                cancellationToken);
            return Json(new { success = true, message = "Kullanici guncellendi." });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    private static bool TryParseRole(string value, out UserRole role) =>
        Enum.TryParse(value, true, out role) && Enum.IsDefined(role);

    private static bool TryParsePermissions(
        IReadOnlyCollection<string> values,
        out IReadOnlyCollection<UserPermission> permissions)
    {
        var parsedPermissions = new List<UserPermission>();

        foreach (var value in values)
        {
            if (!Enum.TryParse(value, true, out UserPermission permission) || !Enum.IsDefined(permission))
            {
                permissions = Array.Empty<UserPermission>();
                return false;
            }

            parsedPermissions.Add(permission);
        }

        permissions = parsedPermissions.Distinct().ToArray();
        return true;
    }
}
