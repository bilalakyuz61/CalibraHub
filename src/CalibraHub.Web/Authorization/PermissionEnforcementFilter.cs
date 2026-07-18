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
/// 4. [PermissionAction] AÇIK kod verilmişse onu kullan (isim tahminini atla).
/// 5. Aksi halde HTTP method + action name → adayActionCodes listesi:
///       GET                                     → [VIEW, VIEW_OWN]
///       POST + Delete/Remove                    → [DELETE_OWN, DELETE_ALL]
///       POST + operasyonel sinyal (whitelist)   → kontrol yok
///       POST + salt-okunur (preview/export/test) → [VIEW, VIEW_OWN]
///       POST (DİĞER HEPSİ)                      → [CREATE, EDIT_OWN, EDIT_ALL]  ← FAIL-CLOSED
///       PUT/PATCH                               → [EDIT_OWN, EDIT_ALL]
///       DELETE                                  → [DELETE_OWN, DELETE_ALL]
/// 6. CheckAnyAsync ile en az birinin izinli olup olmadığını kontrol et.
/// 7. İzinsiz → 403 (JSON request ise JSON, değilse ForbidResult).
///
/// **2026-07-18 — FAIL-OPEN açığı kapatıldı (KRİTİK):** Önceki sürümde adı bilinen kalıba
/// (save/update/create/edit/delete...) uymayan POST'lar `Array.Empty&lt;string&gt;()` dönüp izin
/// kontrolünden SESSİZCE geçiyordu. 433 POST action'ın 172'si bu durumdaydı; aralarında
/// ChangeStatus, Convert*/Deliver*/Receive*, Revise, ApproveStep/RejectStep, StockIn/StockOut/
/// Transfer, ApplyInventoryCount, ShopFloor*, DeactivateUser/Company gibi stok-, onay- ve
/// üretim-etkili gerçek mutasyonlar vardı. Artık varsayılan **mutasyon kabul edilir** ve
/// CREATE/EDIT aranır. Ada göre tahmin kırılgan olduğundan, farklı kod gereken action
/// <see cref="PermissionActionAttribute"/> ile AÇIKÇA işaretlenmelidir.
///
/// **Etki notu:** SystemAdmin (adım 3) ve DepartmentManager (PermissionService.CheckAsync
/// içindeki bilinçli bypass) etkilenmez — kısıt yalnızca normal rollerde devreye girer.
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

        // 6) AÇIK [PermissionAction] varsa isim tahminini atla; yoksa method+ad kalıbı.
        var explicitAction = context.ActionDescriptor.EndpointMetadata
            .OfType<PermissionActionAttribute>().FirstOrDefault();
        var actionCodes = explicitAction is not null
            ? explicitAction.ActionCodes
            : ResolveActionCodes(context);

        // Boş liste = bilinçli muafiyet (operasyonel sinyal veya [PermissionAction] ile
        // açıkça muaf tutulmuş). Ada uymayan POST'lar artık BURAYA DÜŞMEZ — fail-closed.
        if (actionCodes.Length == 0) return;

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
            "POST" when IsReadOnlyPostAction(lower) => new[] { "VIEW", "VIEW_OWN" },
            // FAIL-CLOSED (2026-07-18): adı bilinen kalıba uymayan POST artık ATLANMAZ,
            // mutasyon kabul edilip CREATE/EDIT aranır. Save/Update/Create/Edit/Insert/
            // Upsert/Send/Forward bu dalın zaten kapsamındadır (ayrı IsWriteAction dalına
            // gerek kalmadı). Farklı kod gereken action → [PermissionAction] ile açıkça yaz.
            "POST"                                  => new[] { "CREATE", "EDIT_OWN", "EDIT_ALL" },
            "PUT"  or "PATCH"                       => new[] { "EDIT_OWN", "EDIT_ALL" },
            "DELETE"                                => new[] { "DELETE_OWN", "DELETE_ALL" },
            _ => Array.Empty<string>(),
        };
    }

    /// <summary>
    /// Veri YAZMAYAN POST'lar: önizleme, dışa aktarım/yazdırma, çözümleme/hesaplama, bağlantı
    /// testi. Bunlar için CREATE/EDIT istemek salt-izleyen kullanıcıyı gereksiz kilitlerdi →
    /// VIEW yeterli. Liste bilinçli olarak DAR tutulur: emin olunmayan her action fail-closed
    /// tarafında (CREATE/EDIT) kalır.
    /// </summary>
    private static bool IsReadOnlyPostAction(string actionLower) =>
        actionLower.StartsWith("preview")  ||   // Preview, PreviewUrl
        actionLower.StartsWith("resolve")  ||   // ResolveItemCodes/LinePrices/BodySchema — sorgu
        actionLower.StartsWith("validate") ||   // doğrulama, kayıt yok
        actionLower.StartsWith("test")     ||   // bağlantı/erişim testleri — iş verisi yazmaz
        actionLower.StartsWith("get")      ||   // POST Get* = büyük gövdeli sorgu
        actionLower.StartsWith("report")   ||   // ReportExcel — dışa aktarım
        actionLower.Contains("excel")      ||   // SmartBoardExcel
        actionLower == "renderpdf"         ||
        actionLower == "readheaders"       ||
        actionLower == "queryinline";

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

    // NOT: Eski IsWriteAction (save/update/create/edit/insert/upsert/send/forward/post*)
    // kaldırıldı — fail-closed varsayılan zaten aynı kodları (CREATE/EDIT_OWN/EDIT_ALL)
    // döndürdüğü için ayrı bir dal olarak gereksizdi.

    private static bool IsDeleteAction(string actionLower) =>
        actionLower.Contains("delete") ||
        actionLower.Contains("remove") ||
        actionLower.Contains("clearchat"); // WhatsApp chat temizleme

    /// <summary>
    /// Operasyonel POST action'lar: sohbet sinyalleri, okundu işaretleme, sync.
    /// VIEW yetkisi yeterli; CREATE/DELETE izni aranmaz.
    /// </summary>
    /// StartsWith kullanilir ki "*Json" son ekli varyantlar (MarkAllReadJson gibi) da
    /// kapsansin — 2026-07-18 fail-closed gecisinde bunlar yanlislikla CREATE/EDIT
    /// istemeye baslamisti (okundu isaretleme bir mutasyon degil, okuma sinyalidir).
    private static bool IsOperationalAction(string actionLower) =>
        actionLower.StartsWith("markread")        ||
        actionLower.StartsWith("markunread")      ||
        actionLower.StartsWith("markallread")     ||
        actionLower.StartsWith("sendtyping")      ||
        actionLower.StartsWith("sendreadreceipt") ||
        actionLower.StartsWith("syncgroups")      ||
        actionLower.StartsWith("synccontacts");

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
