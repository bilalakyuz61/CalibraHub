using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

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

    /// <summary>
    /// Bir flow'un ExtraColumnsView'ındaki kolon tanımlarını döner.
    /// viewName sadece harf/rakam/alt-çizgi/cbv_ prefix'i kabul edilir (SQL injection koruması).
    /// Ayrıca ApprovalFlow.ExtraColumnsView'da kayıtlı olması gerekir — whitelist kontrolü.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> FlowExtraColumns(string? viewName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(viewName))
            return Json(new { ok = true, columns = Array.Empty<object>() });

        // Güvenlik: sadece basit view adları (alfanumerik + alt-çizgi)
        if (!Regex.IsMatch(viewName, @"^[A-Za-z][A-Za-z0-9_]{0,127}$"))
            return Json(new { ok = false, error = "Geçersiz view adı." });

        // Whitelist: view adının herhangi bir flow'da kayıtlı olması zorunlu
        var list = await _service.GetListAsync(PendingApprovalScope.All, null, ct);
        var knownViews = list
            .Where(i => !string.IsNullOrWhiteSpace(i.ExtraColumnsViewName))
            .Select(i => i.ExtraColumnsViewName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!knownViews.Contains(viewName))
            return Json(new { ok = false, error = "Bu view bir akışa bağlı değil." });

        var columns = await _service.GetViewColumnMetaAsync(viewName, ct);
        return Json(new { ok = true, columns });
    }

    /// <summary>
    /// Verilen view'dan belirtilen instanceId'lere ait satir degerlerini doner.
    /// ids: virgülle ayrılmış int listesi (örn. "1,2,3").
    /// viewName: FlowExtraColumns ile aynı whitelist dogrulamasina tabidir.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> FlowExtraData(string? viewName, string? ids, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(viewName) || string.IsNullOrWhiteSpace(ids))
            return Json(new { ok = true, data = new { } });

        if (!Regex.IsMatch(viewName, @"^[A-Za-z][A-Za-z0-9_]{0,127}$"))
            return Json(new { ok = false, error = "Geçersiz view adı." });

        // instanceIds — yalnızca rakam ve virgül beklenir
        if (!Regex.IsMatch(ids, @"^[0-9,]+$"))
            return Json(new { ok = false, error = "Geçersiz id listesi." });

        var instanceIds = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                             .Select(s => int.TryParse(s, out var i) ? i : 0)
                             .Where(i => i > 0)
                             .Distinct()
                             .ToArray();
        if (instanceIds.Length == 0)
            return Json(new { ok = true, data = new { } });

        // Whitelist: viewName herhangi bir akışa bağlı olmalı
        var list = await _service.GetListAsync(PendingApprovalScope.All, null, ct);
        var knownViews = list
            .Where(i => !string.IsNullOrWhiteSpace(i.ExtraColumnsViewName))
            .Select(i => i.ExtraColumnsViewName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!knownViews.Contains(viewName))
            return Json(new { ok = false, error = "Bu view bir akışa bağlı değil." });

        var data = await _service.GetViewRowDataAsync(viewName, instanceIds, ct);
        return Json(new { ok = true, data });
    }
}
