using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Proje görev katmanı (AR-GE Görevler sekmesi + Görevlerim + görev şablonları).
/// Yetki: FormCodes.ProjectTask ("PROJECT_TASK") — PermissionEnforcementFilter GET→VIEW,
/// POST→CREATE/EDIT, Delete*→DELETE otomatik eşler; *Lookup uçları cross-form muaftır.
/// EDIT scope'u "All" olmayan kullanıcı yalnız kendisine atanmış görevin durumunu değiştirebilir.
/// </summary>
[Authorize]
[PermissionScope(FormCodes.ProjectTask)]
public sealed class ProjectTaskController : Controller
{
    private readonly IProjectTaskService _tasks;
    private readonly IPermissionService _permissions;
    private readonly ILogger<ProjectTaskController> _logger;

    public ProjectTaskController(IProjectTaskService tasks, IPermissionService permissions, ILogger<ProjectTaskController> logger)
    {
        _tasks = tasks;
        _permissions = permissions;
        _logger = logger;
    }

    // ── Görevler (ProjectEdit "Görevler" sekmesi) ───────────────────────────

    // GET /ProjectTask/List?projectId= → JSON { sequentialTasks, computedProgress, tasks[] }
    [HttpGet]
    public async Task<IActionResult> List(int projectId, CancellationToken ct)
        => Json(await _tasks.ListAsync(projectId, ct));

    // POST /ProjectTask/Save → JSON (görev ekle/güncelle; atama değişince bildirim)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveProjectTaskRequest? request, CancellationToken ct)
    {
        if (request is null) return Json(new { ok = false, error = "Geçersiz istek." });
        try
        {
            var (ok, error, id) = await _tasks.SaveAsync(request, CurrentUserId(), ct);
            return Json(new { ok, error, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Görev kaydetme hatası (projectId={ProjectId}, taskId={TaskId})", request.ProjectId, request.Id);
            return Json(new { ok = false, error = "Görev kaydedilirken bir hata oluştu." });
        }
    }

    // POST /ProjectTask/ChangeStatus → JSON (Tamamla / İptal / Yeniden Aç)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus([FromBody] ChangeProjectTaskStatusRequest? request, CancellationToken ct)
    {
        if (request is null) return Json(new { ok = false, error = "Geçersiz istek." });
        try
        {
            var restrict = await ShouldRestrictToAssigneeAsync(ct);
            var (ok, error) = await _tasks.ChangeStatusAsync(request, CurrentUserId(), restrict, ct);
            return Json(new { ok, error });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Görev durum değişikliği hatası (taskId={TaskId})", request.TaskId);
            return Json(new { ok = false, error = "Görev durumu değiştirilirken bir hata oluştu." });
        }
    }

    // POST /ProjectTask/DeleteTaskJson?id= → JSON (soft-delete)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTaskJson(int id, CancellationToken ct)
    {
        try
        {
            var (ok, error) = await _tasks.DeleteAsync(id, CurrentUserId(), ct);
            return Json(new { ok, error });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Görev silme hatası (taskId={TaskId})", id);
            return Json(new { ok = false, error = "Görev silinirken bir hata oluştu." });
        }
    }

    // POST /ProjectTask/SetSequential → JSON (proje sıralı ilerleme anahtarı)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetSequential([FromBody] SetSequentialTasksRequest? request, CancellationToken ct)
    {
        if (request is null) return Json(new { ok = false, error = "Geçersiz istek." });
        try
        {
            var (ok, error) = await _tasks.SetSequentialAsync(request, CurrentUserId(), ct);
            return Json(new { ok, error });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sıralı ilerleme anahtarı hatası (projectId={ProjectId})", request.ProjectId);
            return Json(new { ok = false, error = "Ayar kaydedilirken bir hata oluştu." });
        }
    }

    // POST /ProjectTask/ApplyTemplate → JSON (şablondan görev seti uygula)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyTemplate([FromBody] ApplyTaskTemplateRequest? request, CancellationToken ct)
    {
        if (request is null) return Json(new { ok = false, error = "Geçersiz istek." });
        try
        {
            var (ok, error, created, sequentialEnabled) = await _tasks.ApplyTemplateAsync(request, CurrentUserId(), ct);
            return Json(new { ok, error, created, sequentialEnabled });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Şablon uygulama hatası (projectId={ProjectId}, templateId={TemplateId})", request.ProjectId, request.TemplateId);
            return Json(new { ok = false, error = "Şablon uygulanırken bir hata oluştu." });
        }
    }

    // ── Görevlerim ──────────────────────────────────────────────────────────

    // GET /ProjectTask/MyTasks → sayfa (Views/ProjectTask/MyTasks.cshtml)
    [HttpGet]
    public IActionResult MyTasks() => View();

    // GET /ProjectTask/MyTasksData → JSON (kişiye atanmış görevler)
    [HttpGet]
    public async Task<IActionResult> MyTasksData(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Json(Array.Empty<MyProjectTaskItem>());
        return Json(await _tasks.ListMineAsync(userId.Value, ct));
    }

    // ── Şablonlar ───────────────────────────────────────────────────────────

    // GET /ProjectTask/Templates → sayfa (Views/ProjectTask/Templates.cshtml)
    [HttpGet]
    public IActionResult Templates() => View();

    // GET /ProjectTask/TemplatesData → JSON (şablonlar, satırlarıyla)
    [HttpGet]
    public async Task<IActionResult> TemplatesData(CancellationToken ct)
        => Json(await _tasks.ListTemplatesAsync(ct));

    // POST /ProjectTask/SaveTemplate → JSON (şablon + satırlar birlikte)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTemplate([FromBody] SaveProjectTaskTemplateRequest? request, CancellationToken ct)
    {
        if (request is null) return Json(new { ok = false, error = "Geçersiz istek." });
        try
        {
            var (ok, error, id) = await _tasks.SaveTemplateAsync(request, CurrentUserId(), ct);
            return Json(new { ok, error, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Şablon kaydetme hatası (templateId={TemplateId})", request.Id);
            return Json(new { ok = false, error = "Şablon kaydedilirken bir hata oluştu." });
        }
    }

    // POST /ProjectTask/DeleteTemplateJson?id= → JSON (soft-delete)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTemplateJson(int id, CancellationToken ct)
    {
        try
        {
            var (ok, error) = await _tasks.DeleteTemplateAsync(id, CurrentUserId(), ct);
            return Json(new { ok, error });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Şablon silme hatası (templateId={TemplateId})", id);
            return Json(new { ok = false, error = "Şablon silinirken bir hata oluştu." });
        }
    }

    // ── Lookup uçları (cross-form muaf — dropdown besleme) ──────────────────

    // GET /ProjectTask/UsersLookup → atanabilir kullanıcılar
    [HttpGet]
    public async Task<IActionResult> UsersLookup(CancellationToken ct)
        => Json(await _tasks.GetAssignableUsersAsync(ct));

    // GET /ProjectTask/TemplatesLookup → şablon dropdown'u { id, label, lineCount }
    [HttpGet]
    public async Task<IActionResult> TemplatesLookup(CancellationToken ct)
    {
        var templates = await _tasks.ListTemplatesAsync(ct);
        return Json(templates.Select(t => new { id = t.Id, label = t.Name, lineCount = t.Lines.Count }));
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────────

    private int? CurrentUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>
    /// EDIT erişim kapsamı "All" değilse kullanıcı yalnız kendisine atanmış görevlerin
    /// durumunu değiştirebilir (HttpPendingApprovalAuthority "Mine" scope deseni).
    /// </summary>
    private async Task<bool> ShouldRestrictToAssigneeAsync(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null or <= 0) return true;

        var roleStr = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var role = roleStr switch
        {
            "SystemAdmin"       => UserRole.SystemAdmin,
            "DepartmentManager" => UserRole.DepartmentManager,
            "Approver"          => UserRole.Approver,
            _                   => UserRole.Operator,
        };
        var deptStr = User.FindFirstValue("department_id");
        int? deptId = int.TryParse(deptStr, out var d) && d > 0 ? d : null;

        var scope = await _permissions.GetAccessScopeAsync(userId.Value, role, deptId, FormCodes.ProjectTask, "EDIT", ct);
        return scope != AccessScope.All;
    }
}
