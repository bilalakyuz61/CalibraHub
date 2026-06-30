using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// 2026-05-26 — "Onayda Bekleyenler" ekran controller'i.
/// Sol panel: belge turune gore gruplandirilmis sayim.
/// Orta panel: secili turun bekleyen kayitlari.
/// Modal: tek instance detay + onayla/reddet.
/// </summary>
[Authorize]
[Route("PendingApproval/[action]")]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.ApprovalPending)]
public sealed class PendingApprovalController : Controller
{
    private readonly IPendingApprovalService _service;
    private readonly IUserSettingRepository _userSettingRepo;
    private const string ColCfgKey = "ui.pa.col-cfg";

    public PendingApprovalController(IPendingApprovalService service, IUserSettingRepository userSettingRepo)
    {
        _service = service;
        _userSettingRepo = userSettingRepo;
    }

    [HttpGet("/PendingApproval")]
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var scopes = await _service.GetAvailableScopesAsync(ct);
        ViewBag.AvailableScopes = scopes;
        ViewBag.DefaultScope = scopes.Contains(PendingApprovalScope.Mine)
            ? PendingApprovalScope.Mine
            : scopes[0];
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Groups(string? scope, CancellationToken ct)
    {
        var groups = await _service.GetGroupsAsync(scope ?? PendingApprovalScope.Mine, ct);
        return Json(new { ok = true, groups });
    }

    [HttpGet]
    public async Task<IActionResult> List(string? scope, int? documentTypeId, CancellationToken ct)
    {
        var items = await _service.GetListAsync(scope ?? PendingApprovalScope.Mine, documentTypeId, ct);
        return Json(new { ok = true, items });
    }

    [HttpGet]
    public async Task<IActionResult> Detail(int instanceId, string? scope, CancellationToken ct)
    {
        var dto = await _service.GetDetailAsync(instanceId, scope ?? PendingApprovalScope.Mine, ct);
        if (dto is null)
            return Json(new { ok = false, error = "Bu kaydı görüntüleme yetkiniz yok veya artık beklemiyor." });
        return Json(new { ok = true, detail = dto });
    }

    [HttpGet]
    public async Task<IActionResult> CompletedGroups(string? scope, CancellationToken ct)
    {
        var groups = await _service.GetCompletedGroupsAsync(scope ?? PendingApprovalScope.Mine, ct);
        return Json(new { ok = true, groups });
    }

    [HttpGet]
    public async Task<IActionResult> CompletedList(string? scope, int? documentTypeId, CancellationToken ct)
    {
        var items = await _service.GetCompletedListAsync(scope ?? PendingApprovalScope.Mine, documentTypeId, ct);
        return Json(new { ok = true, items });
    }

    [HttpGet]
    public async Task<IActionResult> CompletedDetail(int instanceId, string? scope, CancellationToken ct)
    {
        var dto = await _service.GetCompletedDetailAsync(instanceId, scope ?? PendingApprovalScope.Mine, ct);
        if (dto is null)
            return Json(new { ok = false, error = "Bu kaydı görüntüleme yetkiniz yok veya tamamlanmamış." });
        return Json(new { ok = true, detail = dto });
    }

    [HttpGet]
    public async Task<IActionResult> GetColConfig(CancellationToken ct)
    {
        var uid = CurrentUserId();
        if (!uid.HasValue) return Json(new { config = (string?)null });
        var json = await _userSettingRepo.GetAsync(uid.Value, ColCfgKey, ct);
        return Json(new { config = json });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveColConfig([FromBody] SaveColConfigRequest request, CancellationToken ct)
    {
        var uid = CurrentUserId();
        if (!uid.HasValue) return Json(new { ok = false });
        await _userSettingRepo.SetAsync(uid.Value, ColCfgKey, request.Config, ct);
        return Json(new { ok = true });
    }

    private int? CurrentUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

public sealed record SaveColConfigRequest(string? Config);
