using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// 2026-05-26 — "Onayda Bekleyenler" ekran controller'i.
/// Sol panel: belge turune gore gruplandirilmis sayim.
/// Orta panel: secili turun bekleyen kayitlari.
/// Modal: tek instance detay + onayla/reddet.
/// </summary>
[Authorize]
[Route("PendingApproval/[action]")]
public sealed class PendingApprovalController : Controller
{
    private readonly IPendingApprovalService _service;

    public PendingApprovalController(IPendingApprovalService service)
    {
        _service = service;
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

}
