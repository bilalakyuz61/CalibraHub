using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CalibraHub.Web.Authorization;

/// <summary>
/// 2026-06-06 — Controller action'larına yetki kontrolü ekler. Önce SystemAdmin shortcut,
/// sonra UserPermission(UserId) override, sonra UserPermission(DepartmentId), default deny.
///
/// **Kullanım:**
/// <code>
/// [HttpPost]
/// [RequirePermission("DOCUMENT_NEED", "CREATE")]
/// public Task&lt;IActionResult&gt; CreateNeed(...) { ... }
///
/// [HttpPost]
/// [RequirePermission("DOCUMENT_NEED", "EDIT_OWN", "EDIT_ALL")] // Herhangi biri yeterli
/// public Task&lt;IActionResult&gt; UpdateNeed(int id, ...) {
///     // EDIT_OWN ise controller içinde recordOwnerId == currentUserId doğrula
/// }
/// </code>
///
/// **EDIT_OWN/DELETE_OWN için sahip kontrolü:** Bu attribute sadece izin var/yok cevabı verir.
/// EDIT_OWN izni varsa kullanıcı action'a girer, ama controller body'sinde manuel olarak
/// "bu kayıt benim mi?" kontrolü yapmalı (record.CreatedById == currentUserId).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _formCode;
    private readonly string[] _actionCodes;

    /// <param name="formCode">Form kodu — PermissionDef.FormCode (örn. 'DOCUMENT_NEED').</param>
    /// <param name="actionCodes">Bir veya daha çok action — herhangi biri yeterli.</param>
    public RequirePermissionAttribute(string formCode, params string[] actionCodes)
    {
        _formCode = formCode;
        _actionCodes = actionCodes;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        // Claim'lerden user info çek
        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId) || userId <= 0)
        {
            context.Result = new ForbidResult();
            return;
        }

        // Role claim hem enum adı hem Türkçe label olabilir (AccountController login'de GetRoleLabel kullanıyor).
        var roleStr = user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (!CalibraHub.Application.Security.UserAuthorizationCatalog.TryParseRole(roleStr, out var role))
            role = UserRole.Operator;

        // SystemAdmin shortcut — DB sorgusu yok
        if (role == UserRole.SystemAdmin) return;

        // DepartmentId claim (login sırasında set ediliyor olmalı). Yoksa null.
        var deptStr = user.FindFirstValue("department_id");
        int? departmentId = int.TryParse(deptStr, out var d) && d > 0 ? d : null;

        // PermissionService'i DI'dan çek (her request scoped)
        var permService = context.HttpContext.RequestServices
            .GetService(typeof(IPermissionService)) as IPermissionService;
        if (permService is null)
        {
            // Service yoksa fail-closed — log + deny
            context.Result = new ForbidResult();
            return;
        }

        var ct = context.HttpContext.RequestAborted;
        var allowed = await permService.CheckAnyAsync(userId, role, departmentId, _formCode, _actionCodes, ct);
        if (!allowed)
        {
            // JSON endpoint mi? (Accept: application/json veya request path /api veya XHR)
            var isApi = context.HttpContext.Request.Headers.Accept.ToString().Contains("application/json")
                     || context.HttpContext.Request.Headers["X-Requested-With"].ToString().Equals("XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
            if (isApi)
            {
                context.Result = new JsonResult(new { ok = false, error = $"Yetki yok: {_formCode}:{string.Join('|', _actionCodes)}" })
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                };
            }
            else
            {
                context.Result = new ForbidResult();
            }
        }
    }
}
