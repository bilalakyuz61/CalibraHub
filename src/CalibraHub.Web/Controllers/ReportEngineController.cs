using System.Security.Claims;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Application.Services.Scheduling;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Özel rapor motoru — kayıtlı SQL kaynakları (ReportSource) ve IMemoryCache tabanlı sorgu katmanı.
/// </summary>
[Authorize]
[Route("api/report")]
public sealed class ReportEngineController : Controller
{
    private readonly IReportSourceRepository  _sources;
    private readonly IReportQueryService      _query;
    private readonly IScheduledTaskRepository  _tasks;

    public ReportEngineController(IReportSourceRepository sources, IReportQueryService query, IScheduledTaskRepository tasks)
    {
        _sources = sources;
        _query   = query;
        _tasks   = tasks;
    }

    // ── Kayıtlı kaynaklar ────────────────────────────────────────────────────

    [HttpGet("sources")]
    public async Task<IActionResult> GetSources(CancellationToken ct)
    {
        if (!CanDesign()) return Forbid();
        var list = await _sources.GetAllActiveAsync(ct);
        return Json(list);
    }

    [HttpPost("sources")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSource([FromBody] SaveReportSourceRequest req, CancellationToken ct)
    {
        if (!CanDesign()) return Json(new { ok = false, error = "Yetersiz yetki" });
        if (string.IsNullOrWhiteSpace(req.Name))
            return Json(new { ok = false, error = "Kaynak adı boş olamaz" });
        if (string.IsNullOrWhiteSpace(req.SqlQuery))
            return Json(new { ok = false, error = "SQL sorgusu boş olamaz" });
        try
        {
            var id = await _sources.SaveAsync(req, UserName, ct);
            await _query.InvalidateSourceAsync(id, ct);   // SQL/parametre değişmiş olabilir → eski cache'i at
            await SyncRefreshScheduleAsync(id, req.Materialize, req.RefreshScheduleJson, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    [HttpDelete("sources/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSource(int id, CancellationToken ct)
    {
        if (!CanDesign()) return Json(new { ok = false, error = "Yetersiz yetki" });
        try
        {
            await _sources.DeleteAsync(id, ct);
            var existingTask = await _tasks.GetByNameAsync(RefreshTaskName(id), ct);
            if (existingTask is not null) await _tasks.DeleteAsync(existingTask.Id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>Kaynağı diske materialize eder (Qlik QVD benzeri dosya cache) — sorguyu çalıştırıp dosyaya yazar.</summary>
    [HttpPost("sources/{id:int}/materialize")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MaterializeSource(int id, CancellationToken ct)
    {
        if (!CanRefreshData()) return Json(new { ok = false, error = "Yetersiz yetki — rapor verisi güncelleme yetkisi gerekli" });
        try
        {
            var rows = await _query.MaterializeSourceAsync(id, ct);
            return Json(new { ok = true, rows });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── Sorgu ─────────────────────────────────────────────────────────────────

    /// <summary>Kayıtlı kaynaktan sorgu — SQL hiçbir zaman frontend'e gitmez.</summary>
    [HttpGet("query/source/{sourceId:int}")]
    public async Task<IActionResult> QuerySource(int sourceId, CancellationToken ct)
    {
        if (!CanView()) return Forbid();
        try
        {
            var result = await _query.QuerySourceAsync(sourceId, ct);
            return Json(ToResponse(result));
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>Inline SQL sorgusu — panel tasarımcısından gelir.</summary>
    [HttpPost("query/inline")]
    public async Task<IActionResult> QueryInline([FromBody] InlineQueryRequest req, CancellationToken ct)
    {
        if (!CanView()) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Sql))
            return Json(new { ok = false, error = "SQL boş olamaz" });
        try
        {
            var result = await _query.QueryInlineAsync(req.Sql, req.CacheTtlMinutes, ct);
            return Json(ToResponse(result));
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────

    private static object ToResponse(ReportQueryResult r) => new
    {
        ok        = true,
        columns   = r.Columns,
        rows      = r.Rows,
        rowCount  = r.RowCount,
        fromCache = r.FromCache,
        elapsedMs = r.ElapsedMs,
    };

    private string? UserName => User.Identity?.Name;

    private bool CanView()
    {
        var role = ParseRole();
        return role.HasValue &&
               UserAuthorizationCatalog.GetAllowedPermissions(role.Value).Contains(UserPermission.ViewDashboards);
    }

    private bool CanDesign()
    {
        var role = ParseRole();
        return role.HasValue &&
               UserAuthorizationCatalog.GetAllowedPermissions(role.Value).Contains(UserPermission.DesignDashboards);
    }

    private bool CanRefreshData()
    {
        var role = ParseRole();
        return role.HasValue &&
               UserAuthorizationCatalog.GetAllowedPermissions(role.Value).Contains(UserPermission.RefreshReportData);
    }

    // ── Snapshot otomatik yenileme (zamanlanmış görev senkronu) ───────────────

    private static string RefreshTaskName(int sourceId) => $"report-snapshot-refresh-{sourceId}";

    private int? ResolveCompanyId()
        => int.TryParse(User.FindFirst("company_id")?.Value, out var cid) ? cid : null;

    private static string DayName(int dow) => dow switch
    {
        0 => "Pazar", 1 => "Pazartesi", 2 => "Salı", 3 => "Çarşamba",
        4 => "Perşembe", 5 => "Cuma", 6 => "Cumartesi", _ => "?"
    };

    private static bool IsReportManaged(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson)) return false;
        try
        {
            using var d = JsonDocument.Parse(parametersJson);
            return d.RootElement.TryGetProperty("managedBy", out var m)
                && m.ValueKind == JsonValueKind.String && m.GetString() == "report";
        }
        catch { return false; }
    }

    private async Task UpsertSnapshotTaskAsync(int sourceId, string taskName, ScheduledTask? existing,
        ScheduleType st, string expr, string desc, bool managedByReport, CancellationToken ct)
    {
        var task = new ScheduledTask
        {
            Id          = existing?.Id ?? 0,
            Name        = taskName,
            Description = $"Snapshot otomatik yenileme — kaynak #{sourceId}",
            Created     = existing?.Created ?? DateTime.UtcNow,
        };
        task.CompanyId           = ResolveCompanyId();
        task.TaskType            = ScheduledTaskType.ReportSnapshotRefresh;
        task.ParametersJson      = managedByReport
            ? $"{{\"sourceId\":{sourceId},\"managedBy\":\"report\"}}"
            : $"{{\"sourceId\":{sourceId}}}";
        task.ScheduleType        = st;
        task.ScheduleExpression  = expr;
        task.ScheduleDescription = desc;
        task.IsEnabled           = true;
        task.NextRunAt           = ScheduleEvaluator.ComputeNextRun(task, DateTime.UtcNow);
        task.Updated             = DateTime.UtcNow;
        await _tasks.SaveAsync(task, ct);
    }

    /// <summary>
    /// Üç durum: (1) Snapshot kapalı → görev yok (canlı SQL). (2) Snapshot açık + yenileme kapalı →
    /// görev VAR, admin yönetir (Manual; admin takvimini koru). (3) Snapshot açık + yenileme açık →
    /// rapor-sahipli görev (managedBy=report), admin'de salt-okunur.
    /// </summary>
    private async Task SyncRefreshScheduleAsync(int sourceId, bool materialize, string? scheduleJson, CancellationToken ct)
    {
        var taskName = RefreshTaskName(sourceId);
        var existing = await _tasks.GetByNameAsync(taskName, ct);

        // (1) Snapshot KAPALI → canlı SQL, görev yok
        if (!materialize)
        {
            if (existing is not null) await _tasks.DeleteAsync(existing.Id, ct);
            return;
        }

        var mode = "off";
        var time = "03:00";
        var weekday = 1;
        if (!string.IsNullOrWhiteSpace(scheduleJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(scheduleJson);
                var r = doc.RootElement;
                if (r.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String)
                    mode = m.GetString() ?? "off";
                if (r.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString()))
                    time = t.GetString()!;
                if (r.TryGetProperty("weekday", out var w) && w.ValueKind == JsonValueKind.Number)
                    weekday = w.GetInt32();
            }
            catch { mode = "off"; }
        }

        // (2) Snapshot AÇIK + Otomatik yenileme KAPALI → görev VAR, admin yönetir
        if (string.IsNullOrWhiteSpace(mode) || mode == "off")
        {
            if (existing is null)
                // admin'in zamanlayacağı Manual görev oluştur
                await UpsertSnapshotTaskAsync(sourceId, taskName, null, ScheduleType.Manual, "", "Elle — admin'den zamanlayın", managedByReport: false, ct);
            else if (IsReportManaged(existing.ParametersJson))
                // rapor-sahipliyken kapatıldı → admin'e devret (Manual + marker kaldır)
                await UpsertSnapshotTaskAsync(sourceId, taskName, existing, ScheduleType.Manual, "", "Elle — admin'den zamanlayın", managedByReport: false, ct);
            // else: zaten admin-yönetimli → dokunma (admin'in takvimini koru)
            return;
        }

        // (3) Snapshot AÇIK + Otomatik yenileme AÇIK → rapor-sahipli, admin salt-okunur
        ScheduleType st;
        string expr, desc;
        switch (mode)
        {
            case "hourly": st = ScheduleType.Interval;     expr = "3600";              desc = "Saatte bir";                           break;
            case "weekly": st = ScheduleType.WeeklyOnDays; expr = $"{time}|{weekday}"; desc = $"Her hafta {DayName(weekday)} {time}";  break;
            default:       st = ScheduleType.DailyAt;      expr = time;                desc = $"Her gün {time}";                      break;
        }
        await UpsertSnapshotTaskAsync(sourceId, taskName, existing, st, expr, desc, managedByReport: true, ct);
    }

    private UserRole? ParseRole()
    {
        var s = User.FindFirstValue(ClaimTypes.Role);
        return UserAuthorizationCatalog.TryParseRole(s, out var r) ? r : null;
    }
}

public sealed class InlineQueryRequest
{
    public string Sql            { get; set; } = string.Empty;
    public int    CacheTtlMinutes { get; set; } = 5;
}
