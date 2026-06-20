using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CalibraHub.Web.Authorization;

/// <summary>
/// 2026-06-07 — Global action filter. PermissionScope attribute taşıyan controller/action'larda
/// HTTP method + action name pattern'ine göre otomatik (formCode, actionCode) çözümü yapar ve
/// IPermissionService.CheckAnyAsync ile doğrular. Reddederse 403 döner.
///
/// **Algoritma:**
/// 1. [AllowAnonymous] varsa → atla.
/// 2. PermissionScope attribute (action öncelikli, sonra controller) bul. Yoksa → atla.
/// 3. SystemAdmin → izin ver.
/// 4. HTTP method + action name → adayActionCodes listesi:
///       GET                                     → [VIEW]
///       POST + Save/Update/Create/Edit          → [CREATE, EDIT_OWN, EDIT_ALL]
///       POST/DELETE + Delete/Remove             → [DELETE_OWN, DELETE_ALL]
///       Diğer                                   → atla (özel BUTTON:* aksiyonları için manuel)
/// 5. CheckAnyAsync ile en az birinin izinli olup olmadığını kontrol et.
/// 6. İzinsiz → 403 (JSON request ise JSON, değilse ForbidResult).
///
/// **EDIT_OWN / DELETE_OWN için sahip kontrolü:** Filter sadece "izin var/yok" cevabı verir.
/// Controller body içinde record.CreatedById == currentUserId doğrulaması yapılmalı (EDIT_OWN
/// izni varsa ama EDIT_ALL yoksa, kullanıcı başkasının kaydını düzenleyemez).
/// </summary>
public sealed class PermissionEnforcementFilter : IAsyncAuthorizationFilter
{
    private readonly IPermissionService _permService;

    public PermissionEnforcementFilter(IPermissionService permService)
    {
        _permService = permService;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // 1) Anonymous endpoint'ler için skip
        if (context.ActionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any())
            return;

        // 2) PermissionScope attribute (önce action, sonra controller)
        var scopeAttr = context.ActionDescriptor.EndpointMetadata
            .OfType<PermissionScopeAttribute>().FirstOrDefault();
        if (scopeAttr is null) return; // Opt-out: scope yoksa filter geçer

        // 3) Auth gerekli
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        // 4) Claim'ler
        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId) || userId <= 0)
        {
            context.Result = new ForbidResult();
            return;
        }

        var roleStr = user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (!UserAuthorizationCatalog.TryParseRole(roleStr, out var role))
            role = UserRole.Operator;

        // 5) SystemAdmin shortcut
        if (role == UserRole.SystemAdmin) return;

        var deptStr = user.FindFirstValue("department_id");
        int? departmentId = int.TryParse(deptStr, out var d) && d > 0 ? d : null;

        // 6) HTTP method + action name → adayActionCodes
        var actionCodes = ResolveActionCodes(context);
        if (actionCodes.Length == 0) return; // Convention dışı, izin kontrolü yok

        // 7) Check
        var ct = context.HttpContext.RequestAborted;
        var allowed = await _permService.CheckAnyAsync(
            userId, role, departmentId, scopeAttr.FormCode, actionCodes, ct);

        if (!allowed)
            context.Result = MakeForbidResult(context, scopeAttr.FormCode, actionCodes);
    }

    private static string[] ResolveActionCodes(AuthorizationFilterContext context)
    {
        var httpMethod = context.HttpContext.Request.Method.ToUpperInvariant();
        var actionName = (context.ActionDescriptor as ControllerActionDescriptor)?.ActionName ?? string.Empty;
        var lower = actionName.ToLowerInvariant();

        // Cross-form lookup/reference endpoints: başka formlar bu veriyi dropdown/guide olarak kullanır.
        // Kaynak formda VIEW yetkisi olmasa da seçim engellenmez — kullanıcı kural.
        if (httpMethod == "GET" && IsCrossFormReadAction(lower))
            return Array.Empty<string>();

        return httpMethod switch
        {
            // VIEW_OWN (İzleme Özel) da liste sayfasına erişimi açmalı — sayfa yalnızca
            // kullanıcının kendi kayıtlarını gösterir, yine de açılabilir olmalı.
            "GET"  => new[] { "VIEW", "VIEW_OWN" },
            "POST" when IsDeleteAction(lower)       => new[] { "DELETE_OWN", "DELETE_ALL" },
            "POST" when IsOperationalAction(lower)  => Array.Empty<string>(), // okundu/typing/sync — VIEW yeterli
            "POST" when IsWriteAction(lower)        => new[] { "CREATE", "EDIT_OWN", "EDIT_ALL" },
            "PUT"  or "PATCH"                       => new[] { "EDIT_OWN", "EDIT_ALL" },
            "DELETE"                                => new[] { "DELETE_OWN", "DELETE_ALL" },
            _ => Array.Empty<string>(),
        };
    }

    /// <summary>
    /// Başka formların dropdown/guide olarak tükettiği salt-okunur GET endpoint'leri.
    /// Kaynak formda VIEW yetkisi olmasa bile cross-form seçim engellenmez.
    /// </summary>
    private static bool IsCrossFormReadAction(string actionLower) =>
        actionLower.EndsWith("lookup")       ||   // *Lookup  → dropdown/rehber listesi
        actionLower.EndsWith("boardconfig")  ||   // *BoardConfig → C-Grid in-place refresh
        actionLower.EndsWith("search")       ||   // *Search  → autocomplete/arama
        actionLower.EndsWith("autocomplete") ||   // *Autocomplete
        actionLower.EndsWith("options")      ||   // *Options → select option listesi
        actionLower.EndsWith("typeahead");         // *Typeahead

    private static bool IsWriteAction(string actionLower) =>
        actionLower.Contains("save")    ||
        actionLower.Contains("update")  ||
        actionLower.Contains("create")  ||
        actionLower.Contains("edit")    ||
        actionLower.Contains("insert")  ||
        actionLower.Contains("upsert")  ||
        actionLower.Contains("send")    ||   // mesaj/bildirim gönderme (WhatsApp, mail vs.)
        actionLower.Contains("forward") ||   // mesaj iletme
        actionLower.StartsWith("post");

    private static bool IsDeleteAction(string actionLower) =>
        actionLower.Contains("delete") ||
        actionLower.Contains("remove") ||
        actionLower.Contains("clearchat"); // WhatsApp chat temizleme

    /// <summary>
    /// Operasyonel POST action'lar: sohbet sinyalleri, okundu işaretleme, sync.
    /// VIEW yetkisi yeterli; CREATE/DELETE izni aranmaz.
    /// </summary>
    private static bool IsOperationalAction(string actionLower) =>
        actionLower == "markread"        ||
        actionLower == "markunread"      ||
        actionLower == "sendtyping"      ||
        actionLower == "sendreadreceipt" ||
        actionLower == "syncgroups"      ||
        actionLower == "synccontacts";

    private static IActionResult MakeForbidResult(
        AuthorizationFilterContext context, string formCode, string[] actionCodes)
    {
        var req = context.HttpContext.Request;
        // JSON 403 döndürme koşulları: Accept/Content-Type/X-Requested-With/Path tabanlı API tespiti
        var isApi = req.Headers.Accept.ToString().Contains("application/json")
                 || req.Headers["X-Requested-With"].ToString()
                       .Equals("XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                 || req.Path.StartsWithSegments("/api")
                 || (req.ContentType?.Contains("application/json") == true)
                 || req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase); // API çağrıları daima POST
        if (isApi)
        {
            return new JsonResult(new
            {
                ok      = false,
                message = "Bu işlemi yapmak için yetkiniz yok.",   // kullanıcıya sade mesaj
                error   = $"Yetki yok: {formCode}:{string.Join('|', actionCodes)}", // teknik (loglama/debug)
            }) { StatusCode = StatusCodes.Status403Forbidden };
        }
        return new ForbidResult();
    }
}
