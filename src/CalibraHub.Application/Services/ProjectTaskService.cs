using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services;

/// <summary>
/// Proje görev katmanı iş servisi. İş kuralları:
/// - Otomatik ilerleme: pct = 100 × Tamamlandı / (Açık+Tamamlandı); İptal paydadan düşer.
///   Aktif görev yoksa ArgeProject.ProgressPercent'e YAZILMAZ (manuel rejim bozulmaz).
/// - Sıra kilidi: proje SequentialTasks modundayken (OrderNo, Id) sırasına göre önceki
///   Açık görev kapanmadan sonraki tamamlanamaz (repo'da atomik guard — yarış-güvenli).
/// - Atama bildirimi: atanan değişince in-app çan bildirimi (best-effort; hata loglanır,
///   akış bozulmaz — sessiz catch DEĞİL).
/// - Audit: başarılı işlemden SONRA; audit metodları akışı asla bozamaz.
/// </summary>
public sealed class ProjectTaskService : IProjectTaskService
{
    private const string AuditEntity = "ProjectTask";
    private const string AuditTemplateEntity = "ProjectTaskTemplate";

    private readonly IProjectTaskRepository _tasks;
    private readonly IArgeProjectRepository _argeProjects;
    private readonly IUserProfileRepository _users;
    private readonly IUserNotificationRepository _notifications;
    private readonly ILogger<ProjectTaskService> _logger;
    private readonly IAuditTrailService? _audit;

    public ProjectTaskService(
        IProjectTaskRepository tasks,
        IArgeProjectRepository argeProjects,
        IUserProfileRepository users,
        IUserNotificationRepository notifications,
        ILogger<ProjectTaskService> logger,
        IAuditTrailService? audit = null)
    {
        _tasks = tasks;
        _argeProjects = argeProjects;
        _users = users;
        _notifications = notifications;
        _logger = logger;
        _audit = audit;
    }

    public async Task<ProjectTaskListResult> ListAsync(int projectId, CancellationToken ct)
    {
        var sequential = await _tasks.GetSequentialFlagAsync(projectId, ct);
        var items = await _tasks.ListByProjectAsync(projectId, sequential, ct);
        var computed = ComputeProgress(items);
        return new ProjectTaskListResult(sequential, computed, items);
    }

    /// <summary>İlerleme yüzdesini listeden hesaplar (yazmadan). Aktif görev yoksa null.</summary>
    private static decimal? ComputeProgress(IReadOnlyCollection<ProjectTaskDto> items)
    {
        var denom = items.Count(t => t.Status is 0 or 1);
        if (denom == 0) return null;
        var done = items.Count(t => t.Status == 1);
        return Math.Round(100m * done / denom, 2);
    }

    public async Task<(bool Ok, string? Error, int Id)> SaveAsync(SaveProjectTaskRequest request, int? userId, int companyId, CancellationToken ct)
    {
        var title = request.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
            return (false, "Görev başlığı zorunludur.", 0);
        if (title.Length > 200)
            return (false, "Görev başlığı en fazla 200 karakter olabilir.", 0);

        var project = await _argeProjects.GetByDocumentIdAsync(request.ProjectId, ct);
        if (project is null)
            return (false, "Proje bulunamadı.", 0);

        // Atanan kullanıcı doğrulaması (bildirim + FK güvenliği + şirket izolasyonu —
        // Users çok-şirketli, başka şirketin kullanıcısına atama yasak)
        UserProfile? assignee = null;
        if (request.AssignedUserId is > 0)
        {
            assignee = await _users.GetByIdAsync(request.AssignedUserId.Value, ct);
            if (assignee is null || !assignee.IsActive || assignee.CompanyId != companyId)
                return (false, "Atanan kullanıcı bulunamadı veya bu şirkete ait değil.", 0);
        }

        if (request.Id > 0)
        {
            var existing = await _tasks.GetByIdAsync(request.Id, ct);
            if (existing is null || !existing.IsActive)
                return (false, "Görev bulunamadı.", 0);
            if (existing.ProjectId != request.ProjectId)
                return (false, "Görev bu projeye ait değil.", 0);

            var oldSnapshot = new ProjectTask
            {
                Id = existing.Id,
                ProjectId = existing.ProjectId,
                Title = existing.Title,
                Description = existing.Description,
                OrderNo = existing.OrderNo,
                Status = existing.Status,
                AssignedUserId = existing.AssignedUserId,
                TargetDate = existing.TargetDate,
            };

            existing.Title = title;
            existing.Description = NormalizeDescription(request.Description);
            existing.OrderNo = request.OrderNo;
            existing.AssignedUserId = request.AssignedUserId is > 0 ? request.AssignedUserId : null;
            existing.TargetDate = request.TargetDate;
            existing.UpdatedById = userId;

            await _tasks.UpdateAsync(existing, ct);
            await _tasks.RecalcProjectProgressAsync(request.ProjectId, userId, ct);

            _audit?.LogUpdate(AuditEntity, existing.Id, title, oldSnapshot, existing);

            if (existing.AssignedUserId is int newAssignee && newAssignee != oldSnapshot.AssignedUserId)
                await NotifyAssignmentAsync(assignee!, existing.Id, title, project.Name, existing.TargetDate, userId, ct);

            return (true, null, existing.Id);
        }

        var task = new ProjectTask
        {
            ProjectId = request.ProjectId,
            Title = title,
            Description = NormalizeDescription(request.Description),
            OrderNo = request.OrderNo > 0 ? request.OrderNo : await _tasks.GetMaxOrderNoAsync(request.ProjectId, ct) + 1,
            AssignedUserId = request.AssignedUserId is > 0 ? request.AssignedUserId : null,
            TargetDate = request.TargetDate,
            CreatedById = userId,
        };
        var newId = await _tasks.InsertAsync(task, ct);
        await _tasks.RecalcProjectProgressAsync(request.ProjectId, userId, ct);

        _audit?.LogInsert(AuditEntity, newId, title, snapshot: task,
            snapshotIgnore: new[] { nameof(ProjectTask.LinkedEntityKind), nameof(ProjectTask.LinkedEntityId) });

        if (task.AssignedUserId is not null)
            await NotifyAssignmentAsync(assignee!, newId, title, project.Name, task.TargetDate, userId, ct);

        return (true, null, newId);
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Atama bildirimi (in-app çan). Best-effort: hata görev kaydını bozamaz ama sessizce
    /// yutulmaz — server'a loglanır. Kendine atamada bildirim atlanır (gürültü önleme).
    /// </summary>
    private async Task NotifyAssignmentAsync(UserProfile assignee, int taskId, string title, string projectName,
        DateTime? targetDate, int? actorUserId, CancellationToken ct)
    {
        if (assignee.Id == actorUserId) return;
        try
        {
            var body = targetDate is null
                ? projectName
                : $"{projectName} · Hedef: {targetDate:dd.MM.yyyy}";
            await _notifications.AddAsync(new UserNotification
            {
                CompanyId = assignee.CompanyId,
                UserId = assignee.Id,
                Title = $"Yeni görev atandı: {title}",
                Body = body,
                SourceType = "ProjectTask",
                SourceId = taskId,
                Link = $"/ProjectTask/MyTasks?focus={taskId}",
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Görev atama bildirimi yazılamadı (taskId={TaskId}, userId={UserId})", taskId, assignee.Id);
        }
    }

    public async Task<(bool Ok, string? Error)> ChangeStatusAsync(ChangeProjectTaskStatusRequest request, int? userId, bool restrictToAssignee, CancellationToken ct)
    {
        if (!Enum.IsDefined(typeof(ProjectTaskStatus), request.NewStatus))
            return (false, "Geçersiz görev durumu.");
        var newStatus = (ProjectTaskStatus)request.NewStatus;

        var task = await _tasks.GetByIdAsync(request.TaskId, ct);
        if (task is null || !task.IsActive)
            return (false, "Görev bulunamadı.");

        if (restrictToAssignee && task.AssignedUserId != userId)
            return (false, "Yalnızca size atanmış görevlerin durumunu değiştirebilirsiniz.");

        if (task.Status == newStatus)
            return (true, null); // no-op

        var sequential = await _tasks.GetSequentialFlagAsync(task.ProjectId, ct);
        var oldSnapshot = new ProjectTask
        {
            Id = task.Id, ProjectId = task.ProjectId, Title = task.Title, OrderNo = task.OrderNo,
            Status = task.Status, CompletedAt = task.CompletedAt, CompletedByUserId = task.CompletedByUserId,
        };

        switch (newStatus)
        {
            case ProjectTaskStatus.Completed:
            {
                if (task.Status != ProjectTaskStatus.Open)
                    return (false, "Yalnızca açık görevler tamamlanabilir.");

                var completed = await _tasks.TryCompleteGuardedAsync(task.Id, userId, sequential, ct);
                if (!completed)
                {
                    // Guard reddetti — nedeni kesinleştir (yarışta durum değişmiş ya da sıra kilidi).
                    var fresh = await _tasks.GetByIdAsync(task.Id, ct);
                    if (fresh is null || fresh.Status != ProjectTaskStatus.Open)
                        return (false, "Görev bu sırada başka bir kullanıcı tarafından güncellendi.");
                    var blocker = await FindBlockerAsync(fresh, ct);
                    return (false, blocker is null
                        ? "Görev tamamlanamadı."
                        : $"Sıralı ilerleme açık: önce \"{blocker.Title}\" görevi tamamlanmalı.");
                }

                await _tasks.RecalcProjectProgressAsync(task.ProjectId, userId, ct);
                LogStatusChange(oldSnapshot, ProjectTaskStatus.Completed, userId);
                await NotifyNextTaskAsync(task, userId, sequential, ct);
                return (true, null);
            }

            case ProjectTaskStatus.Cancelled:
            {
                if (task.Status != ProjectTaskStatus.Open)
                    return (false, "Yalnızca açık görevler iptal edilebilir.");
                await _tasks.SetStatusAsync(task.Id, (byte)ProjectTaskStatus.Cancelled, userId, ct);
                await _tasks.RecalcProjectProgressAsync(task.ProjectId, userId, ct);
                LogStatusChange(oldSnapshot, ProjectTaskStatus.Cancelled, userId);
                return (true, null);
            }

            case ProjectTaskStatus.Open:
            {
                // Yeniden aç: sıralı modda arkasında tamamlanmış görev varsa zincir bozulur — reddet.
                if (task.Status == ProjectTaskStatus.Completed && sequential
                    && await _tasks.HasCompletedAfterAsync(task.ProjectId, task.OrderNo, task.Id, ct))
                    return (false, "Sıralı ilerleme açık: bu görevden sonra tamamlanmış görevler var, önce onlar yeniden açılmalı.");

                await _tasks.SetStatusAsync(task.Id, (byte)ProjectTaskStatus.Open, userId, ct);
                await _tasks.RecalcProjectProgressAsync(task.ProjectId, userId, ct);
                LogStatusChange(oldSnapshot, ProjectTaskStatus.Open, userId);
                return (true, null);
            }

            default:
                return (false, "Geçersiz görev durumu.");
        }
    }

    /// <summary>Sıralı modda görevin önündeki ilk Açık görevi bulur (kesin hata mesajı için).</summary>
    private async Task<ProjectTask?> FindBlockerAsync(ProjectTask task, CancellationToken ct)
    {
        var items = await _tasks.ListByProjectAsync(task.ProjectId, sequential: true, ct);
        var blocker = items.FirstOrDefault(t => t.Status == 0
            && (t.OrderNo < task.OrderNo || (t.OrderNo == task.OrderNo && t.Id < task.Id)));
        return blocker is null ? null : await _tasks.GetByIdAsync(blocker.Id, ct);
    }

    private void LogStatusChange(ProjectTask oldSnapshot, ProjectTaskStatus newStatus, int? userId)
    {
        var newSnapshot = new ProjectTask
        {
            Id = oldSnapshot.Id, ProjectId = oldSnapshot.ProjectId, Title = oldSnapshot.Title,
            OrderNo = oldSnapshot.OrderNo, Status = newStatus,
            CompletedAt = newStatus == ProjectTaskStatus.Completed ? DateTime.UtcNow : null,
            CompletedByUserId = newStatus == ProjectTaskStatus.Completed ? userId : null,
        };
        _audit?.LogUpdate(AuditEntity, oldSnapshot.Id, oldSnapshot.Title, oldSnapshot, newSnapshot);
    }

    /// <summary>Sıralı modda tamamlanan görevin ardından sıradaki görevin sorumlusuna bildirim.</summary>
    private async Task NotifyNextTaskAsync(ProjectTask completedTask, int? actorUserId, bool sequential, CancellationToken ct)
    {
        if (!sequential) return;
        try
        {
            var next = await _tasks.GetNextOpenTaskAsync(completedTask.ProjectId, completedTask.OrderNo, completedTask.Id, ct);
            if (next?.AssignedUserId is not int nextUserId || nextUserId == actorUserId) return;
            var user = await _users.GetByIdAsync(nextUserId, ct);
            if (user is null || !user.IsActive) return;
            var project = await _argeProjects.GetByDocumentIdAsync(completedTask.ProjectId, ct);
            await _notifications.AddAsync(new UserNotification
            {
                CompanyId = user.CompanyId,
                UserId = nextUserId,
                Title = $"Sıradaki görev sizde: {next.Title}",
                Body = project?.Name,
                SourceType = "ProjectTask",
                SourceId = next.Id,
                Link = $"/ProjectTask/MyTasks?focus={next.Id}",
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sıradaki-görev bildirimi yazılamadı (completedTaskId={TaskId})", completedTask.Id);
        }
    }

    public async Task<(bool Ok, string? Error)> DeleteAsync(int taskId, int? userId, CancellationToken ct)
    {
        var task = await _tasks.GetByIdAsync(taskId, ct);
        if (task is null || !task.IsActive)
            return (false, "Görev bulunamadı.");

        await _tasks.SoftDeleteAsync(taskId, userId, ct);
        await _tasks.RecalcProjectProgressAsync(task.ProjectId, userId, ct);
        _audit?.LogDelete(AuditEntity, taskId, task.Title);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> SetSequentialAsync(SetSequentialTasksRequest request, int? userId, CancellationToken ct)
    {
        var project = await _argeProjects.GetByDocumentIdAsync(request.ProjectId, ct);
        if (project is null)
            return (false, "Proje bulunamadı.");
        var current = await _tasks.GetSequentialFlagAsync(request.ProjectId, ct);
        if (current == request.Enabled)
            return (true, null);

        await _tasks.SetSequentialFlagAsync(request.ProjectId, request.Enabled, userId, ct);
        _audit?.LogChanges("arge_proje", request.ProjectId, project.Name, new[]
        {
            new AuditFieldChange("SequentialTasks", "Sıralı İlerleme", current ? "Açık" : "Kapalı", request.Enabled ? "Açık" : "Kapalı"),
        });
        return (true, null);
    }

    public async Task ReassertComputedProgressAsync(int projectId, int? userId, CancellationToken ct)
    {
        // Görevli projede son yazan görev-hesabıdır; görevsiz projede repo NULL döner (no-op).
        // Hata proje kaydetmeyi bozamaz — ama sessizce de yutulmaz.
        try
        {
            await _tasks.RecalcProjectProgressAsync(projectId, userId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Proje ilerleme yeniden hesabı başarısız (projectId={ProjectId})", projectId);
        }
    }

    public Task<IReadOnlyCollection<MyProjectTaskItem>> ListMineAsync(int userId, CancellationToken ct)
        => _tasks.ListMineAsync(userId, ct);

    public Task<IReadOnlyCollection<ProjectTaskUserOption>> GetAssignableUsersAsync(int companyId, CancellationToken ct)
        => companyId > 0
            ? _tasks.GetAssignableUsersAsync(companyId, ct)
            : Task.FromResult<IReadOnlyCollection<ProjectTaskUserOption>>(Array.Empty<ProjectTaskUserOption>()); // şirket claim'i yoksa fail-safe: boş liste

    // ── Şablonlar ───────────────────────────────────────────────────────────

    public Task<IReadOnlyCollection<ProjectTaskTemplateDto>> ListTemplatesAsync(CancellationToken ct)
        => _tasks.ListTemplatesAsync(ct);

    public async Task<(bool Ok, string? Error, int Id)> SaveTemplateAsync(SaveProjectTaskTemplateRequest request, int? userId, int companyId, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return (false, "Şablon adı zorunludur.", 0);
        if (name.Length > 200)
            return (false, "Şablon adı en fazla 200 karakter olabilir.", 0);

        var lines = (request.Lines ?? Array.Empty<ProjectTaskTemplateLineDto>())
            .Where(l => !string.IsNullOrWhiteSpace(l.Title))
            .ToList();
        if (lines.Count == 0)
            return (false, "Şablonda en az bir görev satırı olmalıdır.", 0);
        if (lines.Any(l => l.Title.Trim().Length > 200))
            return (false, "Görev satırı başlığı en fazla 200 karakter olabilir.", 0);

        // Ad benzersizliği (kullanıcı-girmez-kod kuralı: uniqueness isim üzerinden)
        var all = await _tasks.ListTemplatesAsync(ct);
        if (all.Any(t => t.Id != request.Id && string.Equals(t.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            return (false, $"Aynı isimde başka bir şablon zaten tanımlı: '{name}'", 0);

        // Satır atamaları: yalnız oturum şirketinin aktif kullanıcılarına izin ver
        // (tek sorgu ile doğrula — çapraz-şirket ataması yasak)
        var assignedIds = lines.Where(l => l.DefaultAssignedUserId is > 0)
            .Select(l => l.DefaultAssignedUserId!.Value).Distinct().ToList();
        if (assignedIds.Count > 0)
        {
            var validUsers = (await GetAssignableUsersAsync(companyId, ct)).Select(u => u.Id).ToHashSet();
            var invalid = assignedIds.FirstOrDefault(id => !validUsers.Contains(id));
            if (invalid != 0)
                return (false, "Şablon satırındaki atanan kullanıcı bulunamadı veya bu şirkete ait değil.", 0);
        }

        var template = new ProjectTaskTemplate
        {
            Id = request.Id,
            Name = name,
            Description = NormalizeDescription(request.Description),
            IsSequentialDefault = request.IsSequentialDefault,
            CreatedById = userId,
            UpdatedById = userId,
        };
        var lineEntities = lines
            .OrderBy(l => l.OrderNo)
            .Select((l, i) => new ProjectTaskTemplateLine
            {
                TemplateId = request.Id,
                Title = l.Title.Trim(),
                Description = NormalizeDescription(l.Description),
                OrderNo = i + 1,
                DefaultAssignedUserId = l.DefaultAssignedUserId is > 0 ? l.DefaultAssignedUserId : null,
                CreatedById = userId,
            })
            .ToList();

        var id = await _tasks.UpsertTemplateAsync(template, lineEntities, ct);

        if (request.Id > 0)
            _audit?.LogChanges(AuditTemplateEntity, id, name, new[]
            {
                new AuditFieldChange("Lines", "Görev Satırları", null, $"{lineEntities.Count} satır (topluca yeniden yazıldı)"),
            }, detail: "Şablon güncellendi");
        else
            _audit?.LogInsert(AuditTemplateEntity, id, name, detail: $"{lineEntities.Count} satır");

        return (true, null, id);
    }

    public async Task<(bool Ok, string? Error)> DeleteTemplateAsync(int templateId, int? userId, CancellationToken ct)
    {
        var template = await _tasks.GetTemplateAsync(templateId, ct);
        if (template is null)
            return (false, "Şablon bulunamadı.");
        await _tasks.SoftDeleteTemplateAsync(templateId, userId, ct);
        _audit?.LogDelete(AuditTemplateEntity, templateId, template.Name,
            detail: $"{template.Lines.Count} satır ile birlikte pasiflendi");
        return (true, null);
    }

    public async Task<(bool Ok, string? Error, int Created, bool SequentialEnabled)> ApplyTemplateAsync(ApplyTaskTemplateRequest request, int? userId, int companyId, CancellationToken ct)
    {
        var project = await _argeProjects.GetByDocumentIdAsync(request.ProjectId, ct);
        if (project is null)
            return (false, "Proje bulunamadı.", 0, false);
        var template = await _tasks.GetTemplateAsync(request.TemplateId, ct);
        if (template is null)
            return (false, "Şablon bulunamadı.", 0, false);
        if (template.Lines.Count == 0)
            return (false, "Şablonda uygulanacak görev satırı yok.", 0, false);

        // Mevcut görevlerin ARKASINA eklenir — mükerrer uygulamada sıra çakışmaz.
        // Satırda varsayılan atanan varsa görev atanmış doğar (Daysil 3.3.8.2.3).
        var baseOrder = await _tasks.GetMaxOrderNoAsync(request.ProjectId, ct);
        var tasks = template.Lines
            .OrderBy(l => l.OrderNo)
            .Select((l, i) => new ProjectTask
            {
                ProjectId = request.ProjectId,
                Title = l.Title,
                Description = l.Description,
                OrderNo = baseOrder + i + 1,
                AssignedUserId = l.DefaultAssignedUserId,
                TemplateLineId = l.Id,
                CreatedById = userId,
            })
            .ToList();

        var insertedIds = await _tasks.InsertBatchAsync(tasks, ct);
        var created = insertedIds.Count;
        await _tasks.RecalcProjectProgressAsync(request.ProjectId, userId, ct);

        // Atanmış doğan görevler için bildirim (kişi başına görev başına; kendine-atama atlanır).
        // Kullanıcı kayıtları tek tek çekilir ama önbelleklenir (şablonlar küçük).
        var userCache = new Dictionary<int, UserProfile?>();
        for (var i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            if (task.AssignedUserId is not int uid) continue;
            if (!userCache.TryGetValue(uid, out var assignee))
            {
                assignee = await _users.GetByIdAsync(uid, ct);
                userCache[uid] = assignee;
            }
            // Şirket dışı/pasif atanan (eski şablon kaydı olabilir) → bildirim atlanır
            if (assignee is null || !assignee.IsActive || assignee.CompanyId != companyId) continue;
            await NotifyAssignmentAsync(assignee, insertedIds[i], task.Title, project.Name, task.TargetDate, userId, ct);
        }

        // Şablon sıralı-varsayılan ise ve projede kapalıysa aç — cevapta bildirilir (sessiz yan etki yok).
        var sequentialEnabled = false;
        if (template.IsSequentialDefault && !await _tasks.GetSequentialFlagAsync(request.ProjectId, ct))
        {
            await _tasks.SetSequentialFlagAsync(request.ProjectId, true, userId, ct);
            sequentialEnabled = true;
        }

        if (_audit is not null)
        {
            foreach (var task in tasks)
                _audit.LogInsert(AuditEntity, null, task.Title, detail: $"Şablondan: {template.Name}");
        }

        return (true, null, created, sequentialEnabled);
    }
}
