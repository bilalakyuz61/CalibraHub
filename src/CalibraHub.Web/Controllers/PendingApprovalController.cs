using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// 2026-05-26 — "Onayda Bekleyenler" ekran controller'i.
/// 2026-08-01 — Sol panel/sutun-ayarlari kaldirildi (CalibraSmartBoard donusumu,
/// bkz. BoardConfig). Modal: tek instance detay + onayla/reddet (bespoke, korunan).
/// </summary>
[Authorize]
[Route("PendingApproval/[action]")]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.ApprovalPending)]
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

    /// <summary>
    /// 2026-08-01 — CalibraSmartBoard donusumu: SmartBoard config JSON'i.
    /// scope = mine|department|all (bos ise "mine"); mode = pending|completed (bos ise "pending").
    /// Yetki/kapsam cozumlemesi tamamen mevcut servise devredilir (EnsureScopeAllowedAsync
    /// GetListAsync/GetCompletedListAsync icinde calisir) — burada ek kontrol yok.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> BoardConfig(string? scope, string? mode, CancellationToken ct)
    {
        var resolvedScope = string.IsNullOrWhiteSpace(scope) ? PendingApprovalScope.Mine : scope;
        var isCompleted = string.Equals(mode, "completed", StringComparison.OrdinalIgnoreCase);

        var items = isCompleted
            ? await _service.GetCompletedListAsync(resolvedScope, null, ct)
            : await _service.GetListAsync(resolvedScope, null, ct);

        var now = DateTime.UtcNow;
        var entities = items.Select(it => new
        {
            id = it.InstanceId,
            title = it.DocumentNumber,
            subtitle = string.IsNullOrEmpty(it.DocumentTypeName) ? "Belirsiz" : it.DocumentTypeName,
            description = it.FlowName + " • " + it.StepName,
            imageUrl = (string?)null,
            statusBadge = BuildStatusBadge(it, isCompleted, now),
            widgets = new object[]
            {
                new { id = "w_typ",  type = "data", dataType = "text",     label = "Belge Türü",  value = string.IsNullOrEmpty(it.DocumentTypeName) ? "Belirsiz" : it.DocumentTypeName, detail = (string?)null, color = "slate" },
                new { id = "w_cari", type = "data", dataType = "text",     label = "Cari",        value = it.ContactName ?? "—", detail = (string?)null, color = "indigo" },
                new { id = "w_amt",  type = "data", dataType = "numeric",  label = "Tutar",       value = it.GrandTotal, detail = it.CurrencyCode, color = "emerald" },
                new { id = "w_step", type = "data", dataType = "text",     label = "Adım",        value = it.StepPosition + "/" + it.TotalSteps, detail = it.StepName, color = "amber" },
                new { id = "w_apvr", type = "data", dataType = "text",     label = "Onaylayıcı",  value = it.ApproverName ?? "—", detail = (string?)null, color = "violet" },
                new { id = "w_dt",   type = "data", dataType = "datetime", label = isCompleted ? "Tamamlanma" : "Bekliyor", value = it.StepCreated, detail = (string?)null, color = "blue" },
            },
            primaryAction = (object?)null,
            secondaryAction = (object?)null,
            extraActions = new object[]
            {
                new { id = "inspect", type = "trigger", trigger = "paOpenDetail", label = "İncele", icon = "Eye", color = "indigo", payload = new { instanceId = it.InstanceId } },
            },
        }).ToArray();

        var boardKey = isCompleted ? "pending-approval-completed" : "pending-approval-pending";
        var subtitle = isCompleted
            ? $"{entities.Length} tamamlanmış kayıt"
            : $"{entities.Length} bekleyen kayıt";

        var config = new
        {
            boardKey,
            title = "Onay Takibi",
            subtitle,
            icon = "ClipboardList",
            iconColor = "indigo",
            refreshUrl = $"/PendingApproval/BoardConfig?scope={Uri.EscapeDataString(resolvedScope)}&mode={(isCompleted ? "completed" : "pending")}",
            searchPlaceholder = "Belge no, cari, onaylayıcı ara…",
            emptyText = isCompleted ? "Tamamlanmış onay kaydı bulunamadı." : "Bekleyen onay kaydı bulunamadı.",
            actions = Array.Empty<object>(),
            masterWidgets = new object[]
            {
                new { id = "w_typ",  label = "Belge Türü", dataType = "text" },
                new { id = "w_cari", label = "Cari",       dataType = "text" },
                new { id = "w_amt",  label = "Tutar",      dataType = "numeric" },
                new { id = "w_step", label = "Adım",       dataType = "text" },
                new { id = "w_apvr", label = "Onaylayıcı", dataType = "text" },
                new { id = "w_dt",   label = isCompleted ? "Tamamlanma" : "Bekliyor", dataType = "datetime" },
            },
            entities,
        };
        return Json(config);
    }

    private static object? BuildStatusBadge(PendingApprovalItemDto it, bool isCompleted, DateTime now)
    {
        if (isCompleted)
        {
            return it.InstanceStatus switch
            {
                "Approved" => new { label = "Onaylandı", color = "emerald" },
                "Rejected" => new { label = "Reddedildi", color = "rose" },
                _ => null,
            };
        }
        return it.DueDate.HasValue && it.DueDate.Value < now
            ? new { label = "Gecikti", color = "rose" }
            : null;
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

}
