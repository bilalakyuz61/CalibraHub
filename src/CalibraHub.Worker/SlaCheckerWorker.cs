using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Approval;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Worker;

/// <summary>
/// ApprovalFlow Step node'larina tanimli SLA'leri her 5 dakikada bir tarayan worker.
///   1) Pre-warning  — DueDate yakin (slaReminderHoursBefore icinde), email gonder.
///   2) Overdue      — DueDate gecmis, slaAction'a gore reminder/escalate/autoApprove/autoReject.
/// Per-company DB taramasi: SqlServerConnectionFactory HttpContext yoksa system/varsayilan
/// connection'a duser; tek company senaryosunda yeterli. Multi-tenant tam tarama, kayitli
/// company connection registry'sini iterate eden bir genisleme gerektirir (TODO).
/// </summary>
public sealed class SlaCheckerWorker : BackgroundService
{
    private const string TaskName = "Onay SLA Tarayicisi";
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlaCheckerWorker> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    public SlaCheckerWorker(IServiceScopeFactory scopeFactory, ILogger<SlaCheckerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SlaCheckerWorker started — polling every {Min} minutes.", PollInterval.TotalMinutes);

        // Startup: scheduled task metadata
        try
        {
            using var regScope = _scopeFactory.CreateScope();
            var repo = regScope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
            await repo.UpsertRegistrationAsync(new ScheduledTask
            {
                Name                = TaskName,
                Description         = "Onay akisi adim SLA'leri — pre-warning + overdue tetikleyici.",
                ScheduleDescription = "Her 5 dakikada",
                IsEnabled           = true,
            }, stoppingToken);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "ScheduledTask register failed."); }

        while (!stoppingToken.IsCancellationRequested)
        {
            int processed = 0;
            int status = 0; string? msg = null;
            try
            {
                processed = await CheckOnceAsync(stoppingToken);
                msg = processed == 0 ? "Bekleyen SLA yok." : $"{processed} SLA islendi.";
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SLA tarama hatasi.");
                status = 1; msg = ex.Message;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
                var t = await repo.GetByNameAsync(TaskName, stoppingToken);
                if (t is not null)
                    await repo.ReportRunAsync(t.Id, status, msg, null, DateTime.UtcNow.Add(PollInterval), stoppingToken);
            }
            catch { /* swallow */ }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("SlaCheckerWorker stopped.");
    }

    private async Task<int> CheckOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo     = scope.ServiceProvider.GetRequiredService<IApprovalInstanceRepository>();
        var flow     = scope.ServiceProvider.GetRequiredService<IApprovalFlowService>();
        var notif    = scope.ServiceProvider.GetRequiredService<IApprovalNotificationDispatcher>();
        var users    = scope.ServiceProvider.GetRequiredService<IUserProfileRepository>();
        var executor = scope.ServiceProvider.GetService<IApprovalFlowExecutor>();
        var now = DateTime.UtcNow;

        int handled = 0;

        // 0) Fired timers — Timer node bekleme süresi doldu
        if (executor is not null)
        {
            var firedTimers = await repo.GetFiredTimersAsync(now, ct);
            foreach (var t in firedTimers)
            {
                try
                {
                    await repo.MarkTimerFiredAsync(t.RecordId, ct);
                    var timerNodes = await executor.AfterTimerElapsedAsync(t.InstanceId, t.TimerNodeId, ct);
                    _logger.LogInformation("Timer tetiklendi: instance={Iid}, nodeId={Nid}, nodesProcessed={N}",
                        t.InstanceId, t.TimerNodeId, timerNodes);
                    handled++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Timer işleme hatası (record={Id}).", t.RecordId);
                }
            }
        }

        // 1) Pre-warning
        var warnings = await repo.GetPendingWarningsAsync(now, ct);
        foreach (var w in warnings)
        {
            try
            {
                await notif.SendReminderAsync(w, "warn", ct);
                await repo.MarkSlaWarnedAsync(w.StepRecordId, ct);
                handled++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SLA pre-warning hata (record={Id}).", w.StepRecordId);
            }
        }

        // 2) Overdue
        var overdue = await repo.GetOverdueStepsAsync(now, ct);
        foreach (var o in overdue)
        {
            try
            {
                // Graph-based timeout: flow'da "timeout" edge varsa önce executor'a ver.
                var graphNodes = executor is not null
                    ? await executor.AfterTimeoutAsync(o.InstanceId, o.StepOrder, ct)
                    : 0;
                if (graphNodes > 0)
                {
                    _logger.LogInformation("SLA graph timeout tetiklendi: instance={Iid}, step={Ord}, nodes={N}",
                        o.InstanceId, o.StepOrder, graphNodes);
                    await repo.MarkSlaActionAsync(o.StepRecordId, "graphTimeout", ct);
                    handled++;
                    continue;
                }

                // Legacy SLA actions — flow'da timeout edge yok.
                switch ((o.SlaAction ?? "reminder").Trim())
                {
                    case "reminder":
                        await notif.SendReminderAsync(o, "overdue", ct);
                        break;
                    case "escalate":
                        await EscalateAsync(repo, users, notif, o, ct);
                        break;
                    case "autoApprove":
                        await AutoActionAsync(flow, o, isApprove: true, ct);
                        break;
                    case "autoReject":
                        await AutoActionAsync(flow, o, isApprove: false, ct);
                        break;
                    default:
                        _logger.LogWarning("Bilinmeyen slaAction='{Action}' (record={Id}) — reminder olarak yorumlandi.",
                            o.SlaAction, o.StepRecordId);
                        await notif.SendReminderAsync(o, "overdue", ct);
                        break;
                }
                await repo.MarkSlaActionAsync(o.StepRecordId, o.SlaAction ?? "reminder", ct);
                handled++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SLA overdue handle hata (record={Id}, action={Action}).", o.StepRecordId, o.SlaAction);
            }
        }

        if (handled > 0)
            _logger.LogInformation("SLA pass: {Warn} warning, {Overdue} overdue handled.", warnings.Count, overdue.Count);
        return handled;
    }

    // ── Eskale ────────────────────────────────────────────────────────────────
    private async Task EscalateAsync(
        IApprovalInstanceRepository repo,
        IUserProfileRepository users,
        IApprovalNotificationDispatcher notif,
        OverdueStepRecord rec,
        CancellationToken ct)
    {
        var (newId, newName) = await ResolveEscalateTargetAsync(users, rec, ct);
        if (string.IsNullOrWhiteSpace(newId))
        {
            _logger.LogWarning("SLA eskale: hedef cozumlenemedi (record={Id}, type={Type}, to={To}).",
                rec.StepRecordId, rec.SlaEscalateToType, rec.SlaEscalateToId);
            return;
        }

        var newRecordId = await repo.CreateEscalatedStepAsync(rec.StepRecordId, newId!, newName ?? newId!, ct);
        _logger.LogInformation("SLA eskale: record={From} → yeni record={To}, approver={Approver}",
            rec.StepRecordId, newRecordId, newName);

        // Yeni approver'a bildirim — record degisti, ApproverId yeni hedef
        var notifyRec = rec with { StepRecordId = newRecordId, ApproverId = newId, ApproverName = newName };
        await notif.SendReminderAsync(notifyRec, "overdue", ct);
    }

    private async Task<(string? id, string? name)> ResolveEscalateTargetAsync(
        IUserProfileRepository users, OverdueStepRecord rec, CancellationToken ct)
    {
        var type = (rec.SlaEscalateToType ?? "").Trim();
        var to   = (rec.SlaEscalateToId ?? "").Trim();
        switch (type)
        {
            case "SpecificUser":
                if (int.TryParse(to, out var uid))
                {
                    var u = await users.GetByIdAsync(uid, ct);
                    if (u is not null) return (u.Id.ToString(), u.FullName);
                }
                return (to, rec.SlaEscalateToLabel);

            case "Department":
                // Departman uyelerinin tumune bireysel bildirim icin repo'da
                // departman bazli kullanici sorgulama yok — MVP: ilk uyeyi sec.
                // TODO: IUserProfileRepository.GetByDepartmentIdAsync ekle.
                if (int.TryParse(to, out var depId))
                {
                    var all = await users.GetAllAsync(ct);
                    var first = all.FirstOrDefault(u => u.DepartmentId == depId);
                    if (first is not null) return (first.Id.ToString(), first.FullName);
                }
                return (null, null);

            case "Supervisor":
                if (!string.IsNullOrWhiteSpace(rec.ApproverId) && int.TryParse(rec.ApproverId, out var curId))
                {
                    var cur = await users.GetByIdAsync(curId, ct);
                    if (cur?.SupervisorUserId is int sup)
                    {
                        var supUser = await users.GetByIdAsync(sup, ct);
                        if (supUser is not null) return (supUser.Id.ToString(), supUser.FullName);
                    }
                }
                return (null, null);

            default:
                return (to, rec.SlaEscalateToLabel);
        }
    }

    // ── Otomatik onay/red ────────────────────────────────────────────────────
    private async Task AutoActionAsync(
        IApprovalFlowService flowService,
        OverdueStepRecord rec,
        bool isApprove,
        CancellationToken ct)
    {
        var note = isApprove
            ? "SLA otomatik onay (suresi asildi)."
            : $"SLA otomatik red — {rec.SlaRejectReason ?? "neden belirtilmedi"}.";

        if (isApprove)
        {
            await flowService.ApproveStepAsync(new ApproveStepRequest(
                InstanceId: rec.InstanceId,
                ApproverId: "system",
                ApproverName: "SLA Otomatik",
                Note: note), ct);
        }
        else
        {
            await flowService.RejectAsync(new RejectStepRequest(
                InstanceId: rec.InstanceId,
                ApproverId: "system",
                ApproverName: "SLA Otomatik",
                Note: note), ct);
        }

        _logger.LogInformation("SLA otomatik {Action}: instance={Iid}, step={Ord}",
            isApprove ? "onay" : "red", rec.InstanceId, rec.StepOrder);
    }
}
