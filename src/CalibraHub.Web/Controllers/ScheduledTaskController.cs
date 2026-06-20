using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Constants;
using CalibraHub.Web.Authorization;
using CalibraHub.Web.Helpers;
using CalibraHub.Web.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// ScheduledTaskController — Zamanlanmis Gorevler (Scheduled Task) ekrani ve
/// JSON CRUD endpoint'leri (rapor §2.3 AdminController split).
///
/// Tasinmis endpoint'ler:
///   - GET  /Admin/ScheduledTasks                          → SmartBoard view
///   - GET  /Admin/ScheduledTaskEdit?id=                   → edit view
///   - GET  /Admin/ScheduledTaskHistoryView/{id}           → modal partial (gecmis)
///   - GET  /Admin/ScheduledTasks/BoardEntities            → board refresh JSON
///   - GET  /Admin/ScheduledTasks/List                     → polling JSON
///   - POST /Admin/ScheduledTasks/{id}/RunNow              → manual trigger
///   - POST /Admin/ScheduledTasks/{id}/Toggle              → enable/disable
///   - POST /Admin/ScheduledTasks/{id}/Delete              → soft delete
///   - POST /Admin/ScheduledTasks/Save                     → upsert
///   - GET  /Admin/ScheduledTasks/DbViews                  → ViewReport task view dropdown
///   - GET  /Admin/ScheduledTasks/{id}/History             → son N calistirma JSON
/// </summary>
[Authorize]
[PermissionScope(FormCodes.Scheduler)]
public sealed class ScheduledTaskController : Controller
{
    private readonly IScheduledTaskRepository _scheduledTaskRepo;

    public ScheduledTaskController(IScheduledTaskRepository scheduledTaskRepo)
    {
        _scheduledTaskRepo = scheduledTaskRepo;
    }

    [HttpGet("/Admin/ScheduledTasks")]
    public async Task<IActionResult> ScheduledTasks(CancellationToken ct)
    {
        var tasks = await _scheduledTaskRepo.GetAllAsync(ct);
        var boardConfig = BuildScheduledTasksBoardConfig(tasks);
        return View("~/Views/Admin/ScheduledTasks.cshtml", new ScheduledTasksSmartBoardViewModel { BoardConfig = boardConfig });
    }

    [HttpGet("/Admin/ScheduledTaskEdit")]
    public async Task<IActionResult> ScheduledTaskEdit(int? id, CancellationToken ct)
    {
        if (id.HasValue)
        {
            var task = await _scheduledTaskRepo.GetByIdAsync(id.Value, ct);
            if (task is null) return NotFound();
            var all = await _scheduledTaskRepo.GetAllAsync(ct);
            return View("~/Views/Admin/ScheduledTaskEdit.cshtml", new ScheduledTaskEditViewModel
            {
                Id                  = task.Id,
                Name                = task.Name,
                Description         = task.Description,
                TaskType            = (int)task.TaskType,
                ParametersJson      = task.ParametersJson,
                ScheduleType        = (int)task.ScheduleType,
                ScheduleExpression  = task.ScheduleExpression,
                ScheduleDescription = task.ScheduleDescription,
                IsEnabled           = task.IsEnabled,
                PrerequisiteTaskId  = task.PrerequisiteTaskId,
                IsBuiltin           = task.TaskType == CalibraHub.Domain.Enums.ScheduledTaskType.Builtin,
                AllTasks            = all.Where(t => t.Id != task.Id)
                                         .Select(t => (t.Id, t.Name))
                                         .ToList(),
            });
        }

        var allTasks = await _scheduledTaskRepo.GetAllAsync(ct);
        return View("~/Views/Admin/ScheduledTaskEdit.cshtml", new ScheduledTaskEditViewModel
        {
            AllTasks = allTasks.Select(t => (t.Id, t.Name)).ToList(),
        });
    }

    [HttpGet("/Admin/ScheduledTaskHistoryView/{id:int}")]
    public async Task<IActionResult> ScheduledTaskHistoryView(int id,
        [FromServices] IScheduledTaskRunRepository runRepo,
        CancellationToken ct)
    {
        var runs = await runRepo.GetRecentByTaskIdAsync(id, 30, ct);
        return PartialView("~/Views/Admin/_ScheduledTaskHistory.cshtml", runs);
    }

    /// <summary>SmartBoard in-place refresh icin tam board config dondurur.</summary>
    [HttpGet("/Admin/ScheduledTasks/BoardEntities")]
    public async Task<IActionResult> ScheduledTasksBoardEntities(CancellationToken ct)
    {
        var tasks = await _scheduledTaskRepo.GetAllAsync(ct);
        return Json(BuildScheduledTasksBoardConfig(tasks));
    }

    /// <summary>Canli durumu AJAX ile almak icin — auto-refresh yapmak isteyen JS.</summary>
    [HttpGet("/Admin/ScheduledTasks/List")]
    public async Task<IActionResult> ScheduledTasksList(CancellationToken ct)
    {
        var tasks = await _scheduledTaskRepo.GetAllAsync(ct);
        return Json(tasks.Select(t => new
        {
            t.Id,
            t.Name,
            t.Description,
            TaskType     = (int)t.TaskType,
            TaskTypeName = t.TaskType.ToString(),
            t.ParametersJson,
            ScheduleType     = (int)t.ScheduleType,
            ScheduleTypeName = t.ScheduleType.ToString(),
            t.ScheduleExpression,
            t.ScheduleDescription,
            t.IsEnabled,
            t.IsRunning,
            LastRunAtLocal = t.LastRunAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
            t.LastRunStatus,
            t.LastRunMessage,
            t.LastRunDurationMs,
            NextRunAtLocal = t.NextRunAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
            t.PrerequisiteTaskId,
        }));
    }

    /// <summary>Gorevi hemen calistir (MANUAL trigger).</summary>
    [HttpPost("/Admin/ScheduledTasks/{id:int}/RunNow")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScheduledTaskRunNow(int id,
        [FromServices] CalibraHub.Application.Services.Scheduling.IScheduledTaskDispatcher dispatcher,
        CancellationToken ct)
    {
        var (ok, message) = await dispatcher.TriggerNowAsync(id, ct);
        return Json(new { success = ok, message });
    }

    /// <summary>Gorevi etkinlestir/devre disi birak.</summary>
    [HttpPost("/Admin/ScheduledTasks/{id:int}/Toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScheduledTaskToggle(int id, [FromQuery] bool enabled, CancellationToken ct)
    {
        await _scheduledTaskRepo.SetEnabledAsync(id, enabled, ct);
        return Json(new { success = true });
    }

    /// <summary>Gorevi sil (BUILTIN gorevler silinemez — Worker startup'ta tekrar register eder).</summary>
    [HttpPost("/Admin/ScheduledTasks/{id:int}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScheduledTaskDelete(int id, CancellationToken ct)
    {
        var task = await _scheduledTaskRepo.GetByIdAsync(id, ct);
        if (task is null) return Json(new { success = false, message = "Gorev bulunamadi." });
        if (task.TaskType == CalibraHub.Domain.Enums.ScheduledTaskType.Builtin)
            return Json(new { success = false, message = "BUILTIN gorevler silinemez — sadece devre disi birakilabilir." });
        await _scheduledTaskRepo.DeleteAsync(id, ct);
        return Json(new { success = true });
    }

    /// <summary>Yeni gorev ekle veya mevcut gorevi duzenle.</summary>
    [HttpPost("/Admin/ScheduledTasks/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScheduledTaskSave([FromBody] ScheduledTaskSaveRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Json(new { success = false, message = "Gorev adi zorunlu." });

        var taskType = (CalibraHub.Domain.Enums.ScheduledTaskType)req.TaskType;
        if (taskType == CalibraHub.Domain.Enums.ScheduledTaskType.Builtin)
            return Json(new { success = false, message = "BUILTIN gorevler UI'dan eklenemez — Worker kodunda tanimlanir." });

        // CompanyId — UI'dan kayit edilen task hangi sirket icin tanimlandiysa o claim'den alinir.
        int? companyId = null;
        var companyIdClaim = User.FindFirst("company_id")?.Value;
        if (!string.IsNullOrWhiteSpace(companyIdClaim) && int.TryParse(companyIdClaim, out var parsedCompanyId))
            companyId = parsedCompanyId;

        var task = new CalibraHub.Domain.Entities.ScheduledTask
        {
            Id                  = req.Id,
            Name                = req.Name.Trim(),
            Description         = req.Description,
            TaskType            = taskType,
            ParametersJson      = req.ParametersJson,
            ScheduleType        = (CalibraHub.Domain.Enums.ScheduleType)req.ScheduleType,
            ScheduleExpression  = req.ScheduleExpression,
            ScheduleDescription = req.ScheduleDescription,
            IsEnabled           = req.IsEnabled,
            CompanyId           = companyId,
            PrerequisiteTaskId  = req.PrerequisiteTaskId,
        };
        task.NextRunAt = CalibraHub.Application.Services.Scheduling.ScheduleEvaluator.ComputeNextRun(task, DateTime.UtcNow);
        var id = await _scheduledTaskRepo.SaveAsync(task, ct);
        return Json(new { success = true, id });
    }

    /// <summary>Sirketin DB'sindeki user-defined view adlarini doner — ViewReport task form dropdown'u icin.</summary>
    [HttpGet("/Admin/ScheduledTasks/DbViews")]
    public async Task<IActionResult> ScheduledTaskDbViews(
        [FromServices] IDbSchemaRepository dbSchema,
        CancellationToken ct)
    {
        var names = await dbSchema.GetViewNamesAsync(ct);
        return Json(names);
    }

    /// <summary>ReportSnapshotRefresh görevi için kayıtlı rapor kaynağı dropdown'ı.</summary>
    [HttpGet("/Admin/ScheduledTasks/ReportSources")]
    public async Task<IActionResult> ScheduledTaskReportSources(
        [FromServices] CalibraHub.Application.Abstractions.Persistence.IReportSourceRepository sources,
        CancellationToken ct)
    {
        // Yalnızca Snapshot (Materialize) açık kaynaklar — yalnızca onlar için yenileme görevi anlamlı.
        var list = await sources.GetAllActiveAsync(ct);
        return Json(list.Where(s => s.Materialize).Select(s => new { s.Id, s.Name }));
    }

    /// <summary>Bir gorevin son N calistirma gecmisini doner.</summary>
    [HttpGet("/Admin/ScheduledTasks/{id:int}/History")]
    public async Task<IActionResult> ScheduledTaskHistory(int id, int limit,
        [FromServices] IScheduledTaskRunRepository runRepo,
        CancellationToken ct)
    {
        var takeLimit = limit > 0 && limit <= 200 ? limit : 20;
        var runs = await runRepo.GetRecentByTaskIdAsync(id, takeLimit, ct);
        return Json(runs.Select(r => new
        {
            r.Id,
            r.TaskCode,
            StartedAtLocal   = r.StartedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
            CompletedAtLocal = r.CompletedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
            r.Status,
            r.Message,
            r.DurationMs,
            r.ExecutedCommand,
            Trigger     = (int)r.Trigger,
            TriggerName = r.Trigger.ToString(),
        }));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    private static object BuildScheduledTasksBoardConfig(IReadOnlyCollection<CalibraHub.Domain.Entities.ScheduledTask> tasks)
    {
        var masterWidgets = new List<object>
        {
            SmartBoardFilterHelpers.MakeStdWidget("schedule", "Zamanlama",    "text"),
            SmartBoardFilterHelpers.MakeStdWidget("lastRun",  "Son Çalışma",  "text"),
            SmartBoardFilterHelpers.MakeStdWidget("nextRun",  "Sonraki",      "text"),
        };
        return new
        {
            boardKey   = "scheduled-tasks",
            title      = "Zamanlanmış Görevler",
            refreshUrl = "/Admin/ScheduledTasks/BoardEntities",
            actions  = new object[]
            {
                new { id = "new", label = "Yeni Görev", icon = "Plus", variant = "primary", url = "/Admin/ScheduledTaskEdit" },
            },
            masterWidgets,
            entities = tasks.Select(t =>
            {
                var isBuiltin    = t.TaskType == CalibraHub.Domain.Enums.ScheduledTaskType.Builtin;
                var lastRunText  = t.LastRunAt?.ToLocalTime().ToString("dd.MM HH:mm") ?? "—";
                var nextRunText  = t.NextRunAt?.ToLocalTime().ToString("dd.MM HH:mm") ?? "—";
                var toggleUrl    = $"/Admin/ScheduledTasks/{t.Id}/Toggle?enabled={(!t.IsEnabled).ToString().ToLower()}";
                var toggleLabel  = t.IsEnabled ? "Durdur" : "Etkinleştir";
                var toggleIcon   = t.IsEnabled ? "ToggleRight" : "ToggleLeft";
                var toggleColor  = t.IsEnabled ? "orange" : "emerald";
                return (object)new
                {
                    id           = t.Id,
                    title        = t.Name,
                    subtitle     = GetScheduledTaskTypeLabel(t.TaskType),
                    description  = t.Description,
                    statusBadge  = GetScheduledTaskStatusBadge(t),
                    widgets      = new object[]
                    {
                        new { id = "schedule", label = "Zamanlama", value = t.ScheduleDescription ?? "—", icon = "Clock" },
                        new { id = "lastRun",  label = "Son Çalışma", value = lastRunText, icon = "History" },
                        new { id = "nextRun",  label = "Sonraki",    value = nextRunText, icon = "Calendar" },
                    },
                    primaryAction   = isBuiltin ? null : (object?)new { label = "Düzenle", icon = "Edit2", url = $"/Admin/ScheduledTaskEdit?id={t.Id}", hideButton = true },
                    secondaryAction = (object?)null,
                    extraActions    = new object?[]
                    {
                        isBuiltin ? null : (object?)new { label = "Düzenle", icon = "Edit2",   color = "amber",   url    = $"/Admin/ScheduledTaskEdit?id={t.Id}" },
                        isBuiltin ? null : (object?)new { label = "Sil",     icon = "Trash2",  color = "red",     type   = "api-post", url = $"/Admin/ScheduledTasks/{t.Id}/Delete", confirm = $"\"{t.Name}\" görevini silmek istediğinizden emin misiniz?" },
                        new { label = "Şimdi Çalıştır", icon = "Play",       color = "emerald", type = "api-post",    url = $"/Admin/ScheduledTasks/{t.Id}/RunNow" },
                        new { label = toggleLabel,       icon = toggleIcon,   color = toggleColor, type = "api-post", url = toggleUrl },
                        new { label = "Geçmiş",          icon = "History",   color = "blue",    type = "fetch-modal", fetchUrl = $"/Admin/ScheduledTaskHistoryView/{t.Id}", modalTitle = $"{t.Name} — Çalıştırma Geçmişi" },
                    },
                };
            }).ToArray(),
        };
    }

    private static string GetScheduledTaskTypeLabel(CalibraHub.Domain.Enums.ScheduledTaskType type) => type switch
    {
        CalibraHub.Domain.Enums.ScheduledTaskType.Builtin         => "Sistem",
        CalibraHub.Domain.Enums.ScheduledTaskType.SqlProcedure    => "SQL Prosedür",
        CalibraHub.Domain.Enums.ScheduledTaskType.HttpApi         => "HTTP API",
        CalibraHub.Domain.Enums.ScheduledTaskType.FileTransfer    => "Dosya Transfer",
        CalibraHub.Domain.Enums.ScheduledTaskType.CurrencyRefresh => "Kur Güncelleme",
        CalibraHub.Domain.Enums.ScheduledTaskType.ViewReport      => "Rapor",
        CalibraHub.Domain.Enums.ScheduledTaskType.Integration     => "Entegrasyon",
        CalibraHub.Domain.Enums.ScheduledTaskType.ReportSnapshotRefresh => "Rapor Snapshot Yenileme",
        _                                                          => type.ToString(),
    };

    private static object GetScheduledTaskStatusBadge(CalibraHub.Domain.Entities.ScheduledTask t)
    {
        if (t.IsRunning)   return new { label = "Çalışıyor",  color = "blue" };
        if (!t.IsEnabled)  return new { label = "Devre Dışı", color = "gray" };
        return t.LastRunStatus switch
        {
            0 => new { label = "Başarılı", color = "green" },
            1 => (object)new { label = "Hata",     color = "red" },
            _ => new { label = "Bekliyor", color = "gray" },
        };
    }
}

public sealed class ScheduledTaskSaveRequest
{
    public int     Id                  { get; set; }
    public string  Name                { get; set; } = string.Empty;
    public string? Description         { get; set; }
    public int     TaskType            { get; set; }
    public string? ParametersJson      { get; set; }
    public int     ScheduleType        { get; set; }
    public string? ScheduleExpression  { get; set; }
    public string? ScheduleDescription { get; set; }
    public bool    IsEnabled           { get; set; } = true;
    public int?    PrerequisiteTaskId  { get; set; }
}
