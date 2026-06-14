using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Authorization;

/// <summary>
/// 2026-06-06 — Razor view'larda izin gate'i.
///
/// **Kullanım:**
/// <code>
/// @if (await Html.HasPermissionAsync("DOCUMENT_NEED", "CREATE"))
/// {
///     &lt;button&gt;Yeni Kayıt&lt;/button&gt;
/// }
/// </code>
/// </summary>
public static class PermissionHtmlHelper
{
    public static async Task<bool> HasPermissionAsync(
        this IHtmlHelper html, string formCode, string actionCode)
    {
        var ctx = html.ViewContext.HttpContext;
        var user = ctx.User;
        if (user?.Identity?.IsAuthenticated != true) return false;

        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId) || userId <= 0) return false;

        var roleStr = user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (!CalibraHub.Application.Security.UserAuthorizationCatalog.TryParseRole(roleStr, out var role))
            role = UserRole.Operator;

        if (role == UserRole.SystemAdmin) return true;

        var deptStr = user.FindFirstValue("department_id");
        int? departmentId = int.TryParse(deptStr, out var d) && d > 0 ? d : null;

        var svc = ctx.RequestServices.GetService(typeof(IPermissionService)) as IPermissionService;
        if (svc is null) return false;

        return await svc.CheckAsync(userId, role, departmentId, formCode, actionCode, ctx.RequestAborted);
    }

    /// <summary>Herhangi bir action izinli mi? (örn. EDIT_OWN VEYA EDIT_ALL)</summary>
    public static async Task<bool> HasAnyPermissionAsync(
        this IHtmlHelper html, string formCode, params string[] actionCodes)
    {
        var ctx = html.ViewContext.HttpContext;
        var user = ctx.User;
        if (user?.Identity?.IsAuthenticated != true) return false;

        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId) || userId <= 0) return false;

        var roleStr = user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (!CalibraHub.Application.Security.UserAuthorizationCatalog.TryParseRole(roleStr, out var role))
            role = UserRole.Operator;

        if (role == UserRole.SystemAdmin) return true;

        var deptStr = user.FindFirstValue("department_id");
        int? departmentId = int.TryParse(deptStr, out var d) && d > 0 ? d : null;

        var svc = ctx.RequestServices.GetService(typeof(IPermissionService)) as IPermissionService;
        if (svc is null) return false;

        return await svc.CheckAnyAsync(userId, role, departmentId, formCode, actionCodes, ctx.RequestAborted);
    }
}
