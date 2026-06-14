using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Worker;

/// <summary>
/// Yaklaşan bakım/kalibrasyon hatırlatıcısı. ReminderNotificationWorker deseni:
/// ScheduledTask kaydı + periyodik poll. NextMaintenanceDate / NextCalibrationDate
/// lead-window (7 gün) içine giren aktif varlıklar için yöneticilere in-app bildirim yazar.
/// Her planlı tarih için tek kez bildirir (Maintenance/CalibrationRemindedFor izi).
///
/// Not: Worker per-company DB'de tek tenant'a (bağlı şirket) çözülür — mevcut worker'larla
/// aynı sınırlama. Çoklu şirket için tenant döngüsü ileride eklenebilir.
/// </summary>
public sealed class AssetMaintenanceReminderWorker : BackgroundService
{
    private const string TaskName = "Varlık Bakım Hatırlatıcı";
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(6);
    private const int LeadDays = 7;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AssetMaintenanceReminderWorker> _logger;

    public AssetMaintenanceReminderWorker(IServiceScopeFactory scopeFactory, ILogger<AssetMaintenanceReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AssetMaintenanceReminderWorker started.");

        try
        {
            using var regScope = _scopeFactory.CreateScope();
            var repo = regScope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
            await repo.UpsertRegistrationAsync(new ScheduledTask
            {
                Name = TaskName,
                Description = "Yaklaşan bakım/kalibrasyon tarihli varlıklar için yöneticilere bildirim oluşturur.",
                ScheduleDescription = "Her 6 saatte",
                IsEnabled = true,
            }, stoppingToken);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "AssetMaintenance ScheduledTask register failed."); }

        while (!stoppingToken.IsCancellationRequested)
        {
            int processed = 0, status = 0; string? msg = null;
            try
            {
                processed = await ProcessDueAsync(stoppingToken);
                msg = processed == 0 ? "Yaklaşan bakım/kalibrasyon yok." : $"{processed} varlık için hatırlatma gönderildi.";
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing asset maintenance reminders.");
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

        _logger.LogInformation("AssetMaintenanceReminderWorker stopped.");
    }

    private async Task<int> ProcessDueAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var assetRepo = scope.ServiceProvider.GetRequiredService<IAssetRepository>();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserProfileRepository>();
        var notificationRepo = scope.ServiceProvider.GetRequiredService<IUserNotificationRepository>();

        var threshold = DateTime.Today.AddDays(LeadDays);
        var due = await assetRepo.GetAssetsWithDueRemindersAsync(threshold, ct);
        if (due.Count == 0) return 0;

        var managers = (await userRepo.GetAllAsync(ct))
            .Where(u => u.IsActive && (u.Role == UserRole.SystemAdmin || u.Role == UserRole.DepartmentManager))
            .ToArray();
        if (managers.Length == 0)
        {
            _logger.LogWarning("Bakım hatırlatması için bildirim alacak yönetici kullanıcı yok.");
            return 0;
        }

        var count = 0;
        foreach (var a in due)
        {
            try
            {
                var maintDue = a.NextMaintenanceDate.HasValue
                    && a.NextMaintenanceDate.Value.Date <= threshold
                    && (!a.MaintenanceRemindedFor.HasValue || a.MaintenanceRemindedFor.Value.Date != a.NextMaintenanceDate.Value.Date);

                var calibDue = a.NextCalibrationDate.HasValue
                    && a.NextCalibrationDate.Value.Date <= threshold
                    && (!a.CalibrationRemindedFor.HasValue || a.CalibrationRemindedFor.Value.Date != a.NextCalibrationDate.Value.Date);

                if (maintDue)
                {
                    await NotifyAsync(managers, notificationRepo, a, "Bakım", "AssetMaintenance", a.NextMaintenanceDate!.Value, ct);
                    await assetRepo.MarkMaintenanceRemindedAsync(a.Id, a.NextMaintenanceDate!.Value, ct);
                    count++;
                }
                if (calibDue)
                {
                    await NotifyAsync(managers, notificationRepo, a, "Kalibrasyon", "AssetCalibration", a.NextCalibrationDate!.Value, ct);
                    await assetRepo.MarkCalibrationRemindedAsync(a.Id, a.NextCalibrationDate!.Value, ct);
                    count++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Asset {AssetId} reminder failed.", a.Id);
            }
        }
        return count;
    }

    private static async Task NotifyAsync(
        UserProfile[] managers, IUserNotificationRepository repo,
        Asset asset, string label, string sourceType, DateTime dueDate, CancellationToken ct)
    {
        var title = $"{label} yaklaşıyor: {asset.AssetName}";
        var body = $"{asset.AssetCode} kodlu varlığın {label.ToLowerInvariant()} tarihi {dueDate:dd.MM.yyyy}.";
        foreach (var u in managers)
        {
            await repo.AddAsync(new UserNotification
            {
                CompanyId = u.CompanyId,
                UserId = u.Id,
                Title = title,
                Body = body,
                SourceType = sourceType,
                SourceId = null,
                Link = "/Assets/AssetEdit?id=" + asset.Id,
            }, ct);
        }
    }
}
