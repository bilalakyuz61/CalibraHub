using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services.Security;

/// <summary>
/// 2026-06-07 — EDIT_OWN/DELETE_OWN sahip kontrolünü tek metoda indirir.
///
/// **Kullanım (controller body):**
/// <code>
/// var doc = await _repo.GetAsync(id, ct);
/// if (!await _permService.CanEditAsync(userId, role, deptId, "DOCUMENT_NEED", doc.CreatedById, ct))
///     return Forbid();
/// </code>
///
/// **Mantık:**
/// 1) SystemAdmin → true
/// 2) EDIT_ALL/DELETE_ALL izinli → true (başkasının kaydı dahil)
/// 3) EDIT_OWN/DELETE_OWN izinli VE recordOwnerId == userId → true
/// 4) Diğer → false
/// </summary>
public static class PermissionServiceExtensions
{
    public static async Task<bool> CanEditAsync(
        this IPermissionService svc,
        int userId, UserRole role, int? departmentId,
        string formCode, int recordOwnerId,
        CancellationToken ct)
    {
        if (role == UserRole.SystemAdmin) return true;
        // ALL → her zaman
        if (await svc.CheckAsync(userId, role, departmentId, formCode, "EDIT_ALL", ct))
            return true;
        // OWN + sahip
        if (recordOwnerId == userId &&
            await svc.CheckAsync(userId, role, departmentId, formCode, "EDIT_OWN", ct))
            return true;
        return false;
    }

    public static async Task<bool> CanDeleteAsync(
        this IPermissionService svc,
        int userId, UserRole role, int? departmentId,
        string formCode, int recordOwnerId,
        CancellationToken ct)
    {
        if (role == UserRole.SystemAdmin) return true;
        if (await svc.CheckAsync(userId, role, departmentId, formCode, "DELETE_ALL", ct))
            return true;
        if (recordOwnerId == userId &&
            await svc.CheckAsync(userId, role, departmentId, formCode, "DELETE_OWN", ct))
            return true;
        return false;
    }

    /// <summary>
    /// Genel sahiplik kontrolü — herhangi bir action için (ör. BUTTON:APPROVE_OWN).
    /// </summary>
    public static async Task<bool> CanActOnAsync(
        this IPermissionService svc,
        int userId, UserRole role, int? departmentId,
        string formCode, string actionAll, string actionOwn,
        int recordOwnerId, CancellationToken ct)
    {
        if (role == UserRole.SystemAdmin) return true;
        if (await svc.CheckAsync(userId, role, departmentId, formCode, actionAll, ct))
            return true;
        if (recordOwnerId == userId &&
            await svc.CheckAsync(userId, role, departmentId, formCode, actionOwn, ct))
            return true;
        return false;
    }

    /// <summary>
    /// 2026-06-08 — Form içi özel buton yetkisi kontrolü.
    /// <c>FormButtonCatalog.Buttons</c> içinde tanımlı butonlar için Razor:
    /// <code>@if (await PermService.CanInvokeButtonAsync(uid, role, dept, "DOCUMENT_NEED", "APPROVE", ct)) { ... }</code>
    /// SystemAdmin'a daima true. Diğer kullanıcılar için <c>BUTTON:&lt;KEY&gt;</c> izni aranır.
    /// </summary>
    public static Task<bool> CanInvokeButtonAsync(
        this IPermissionService svc,
        int userId, UserRole role, int? departmentId,
        string formCode, string buttonKey, CancellationToken ct)
    {
        if (role == UserRole.SystemAdmin) return Task.FromResult(true);
        var action = $"BUTTON:{buttonKey?.Trim().ToUpperInvariant()}";
        return svc.CheckAsync(userId, role, departmentId, formCode, action, ct);
    }

    /// <summary>
    /// 2026-06-08 — Yetkilendirilebilir alan (widget) görme yetkisi kontrolü.
    /// <c>WidgetDefinition.IsPermissionControlled=true</c> olan widget'lar için Razor:
    /// <code>@if (await PermService.CanViewFieldAsync(uid, role, dept, "CONTACT_EDIT", "TAX_OFFICE", ct)) { ... }</code>
    /// İzin yoksa alan UI'da hiç görünmemeli (gerekirse rendering pipeline alanı filtreler).
    /// SystemAdmin'a daima true.
    /// </summary>
    public static Task<bool> CanViewFieldAsync(
        this IPermissionService svc,
        int userId, UserRole role, int? departmentId,
        string formCode, string widgetCode, CancellationToken ct)
    {
        if (role == UserRole.SystemAdmin) return Task.FromResult(true);
        var action = $"FIELD:{widgetCode?.Trim().ToUpperInvariant()}";
        return svc.CheckAsync(userId, role, departmentId, formCode, action, ct);
    }
}
