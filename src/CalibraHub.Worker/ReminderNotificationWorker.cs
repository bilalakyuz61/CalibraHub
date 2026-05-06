using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Worker;

public sealed class ReminderNotificationWorker : BackgroundService
{
    private const string TaskName = "Hatirlatici Bildirim";
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderNotificationWorker> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    public ReminderNotificationWorker(IServiceScopeFactory scopeFactory, ILogger<ReminderNotificationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReminderNotificationWorker started.");

        // Startup registration
        try
        {
            using var regScope = _scopeFactory.CreateScope();
            var repo = regScope.ServiceProvider.GetRequiredService<IScheduledTaskRepository>();
            await repo.UpsertRegistrationAsync(new ScheduledTask
            {
                Name                = TaskName,
                Description         = "Not'lara bagli zamanlanmis hatirlaticilari kontrol edip suresi gelenleri gonderir.",
                ScheduleDescription = "Her 60 saniyede",
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
                processed = await ProcessDueRemindersAsync(stoppingToken);
                msg = processed == 0 ? "Bekleyen hatirlatici yok." : $"{processed} hatirlatici islendi.";
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing due reminders.");
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

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("ReminderNotificationWorker stopped.");
    }

    private async Task<int> ProcessDueRemindersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var noteRepository = scope.ServiceProvider.GetRequiredService<INoteRepository>();
        var notificationRepo = scope.ServiceProvider.GetRequiredService<IUserNotificationRepository>();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserProfileRepository>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IReminderEmailSender>();

        var dueReminders = await noteRepository.GetUnsentDueRemindersAsync(cancellationToken);
        if (dueReminders.Count == 0) return 0;

        _logger.LogInformation("Processing {Count} due reminder(s).", dueReminders.Count);

        foreach (var (reminder, note) in dueReminders)
        {
            try
            {
                // Hedef listesi: TargetUserIds doluysa o liste, yoksa fallback = notun sahibi
                var targets = reminder.TargetUserIds.Count > 0
                    ? reminder.TargetUserIds.ToArray()
                    : new[] { note.UserId };
                var channel = reminder.DeliveryChannel;
                var subject = "Hatirlatici: " + (string.IsNullOrWhiteSpace(note.Title) ? "Basliksiz Not" : note.Title);

                foreach (var targetUserId in targets.Distinct())
                {
                    // InApp / Both: veritabanina bildirim kaydi yaz
                    if (channel == ReminderDeliveryChannel.InApp || channel == ReminderDeliveryChannel.Both)
                    {
                        var notification = new UserNotification
                        {
                            CompanyId  = note.CompanyId,
                            UserId     = targetUserId,
                            Title      = subject,
                            Body       = $"Bu notun hatirlaticisi {reminder.RemindAt:dd.MM.yyyy HH:mm} icin planlanmisti.",
                            SourceType = "NoteReminder",
                            SourceId   = note.Id,
                            Link       = "/Notes?id=" + note.Id,
                        };
                        await notificationRepo.AddAsync(notification, cancellationToken);
                    }

                    // Email / Both
                    if (channel == ReminderDeliveryChannel.Email || channel == ReminderDeliveryChannel.Both)
                    {
                        var user = await userRepo.GetByIdAsync(targetUserId, cancellationToken);
                        if (user is null)
                        {
                            _logger.LogWarning("Reminder {ReminderId} target user {UserId} bulunamadi — email atilmadi.",
                                reminder.Id, targetUserId);
                            continue;
                        }
                        var body = $"Merhaba {user.FullName},\n\n" +
                                   $"Bu not icin bir hatirlatici zamanlamistiniz:\n\n" +
                                   $"  Baslik: {note.Title}\n" +
                                   $"  Zaman:  {reminder.RemindAt:dd.MM.yyyy HH:mm}\n\n" +
                                   "CalibraHub uzerinden notu acabilirsiniz.";
                        var result = await emailSender.SendAsync(note.CompanyId, user.Email, subject, body, cancellationToken);
                        if (result.Status == ReminderEmailStatus.Sent)
                            _logger.LogInformation("Reminder {ReminderId} email gonderildi ({Email}).", reminder.Id, user.Email);
                        else
                            _logger.LogWarning("Reminder {ReminderId} email {Status}: {Message}",
                                reminder.Id, result.Status, result.Message ?? "(bos)");
                    }
                }

                // Geri uyumluluk — log satiri (toast server tarafinda gosterilemez)
                ShowWindowsToastNotification(note.Title, reminder.RemindAt);

                await noteRepository.MarkReminderSentAsync(reminder.Id, DateTime.Now, cancellationToken);

                if (reminder.RecurrenceType != ReminderRecurrenceType.None)
                {
                    var nextRemindAt = ComputeNextRemindAt(reminder.RemindAt, reminder.RecurrenceType, reminder.RecurrenceData);
                    if (nextRemindAt.HasValue)
                    {
                        var nextReminder = new NoteReminder
                        {
                            NoteId          = reminder.NoteId,
                            RemindAt        = nextRemindAt.Value,
                            RecurrenceType  = reminder.RecurrenceType,
                            RecurrenceData  = reminder.RecurrenceData,
                            DeliveryChannel = reminder.DeliveryChannel,
                            TargetUserIds   = reminder.TargetUserIds,
                        };
                        await noteRepository.AddReminderAsync(nextReminder, cancellationToken);
                        _logger.LogInformation("Next occurrence scheduled for {RemindAt:dd.MM.yyyy HH:mm}.", nextRemindAt.Value);
                    }
                }

                _logger.LogInformation("Reminder {ReminderId} for note '{NoteTitle}' processed.", reminder.Id, note.Title);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process reminder {ReminderId}.", reminder.Id);
            }
        }

        return dueReminders.Count;
    }

    private static DateTime? ComputeNextRemindAt(DateTime current, ReminderRecurrenceType type, string? data)
    {
        return type switch
        {
            ReminderRecurrenceType.Hourly => current.AddHours(1),
            ReminderRecurrenceType.Daily => current.AddDays(1),
            ReminderRecurrenceType.Weekly => current.AddDays(7),
            ReminderRecurrenceType.Monthly => current.AddMonths(1),
            ReminderRecurrenceType.Weekends => NextWeekend(current),
            ReminderRecurrenceType.SpecificDaysOfWeek => NextSpecificDayOfWeek(current, data),
            ReminderRecurrenceType.SpecificDaysOfMonth => NextSpecificDayOfMonth(current, data),
            _ => null
        };
    }

    private static DateTime NextWeekend(DateTime from)
    {
        var next = from.AddDays(1);
        while (next.DayOfWeek != DayOfWeek.Saturday && next.DayOfWeek != DayOfWeek.Sunday)
            next = next.AddDays(1);
        return next.Date.Add(from.TimeOfDay);
    }

    private static DateTime NextSpecificDayOfWeek(DateTime from, string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return from.AddDays(7);

        var days = data.Split(',')
            .Select(d => int.TryParse(d.Trim(), out var v) ? (DayOfWeek?)v : null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToHashSet();

        if (days.Count == 0) return from.AddDays(7);

        var next = from.AddDays(1);
        for (var i = 0; i < 7; i++, next = next.AddDays(1))
        {
            if (days.Contains(next.DayOfWeek))
                return next.Date.Add(from.TimeOfDay);
        }

        return from.AddDays(7);
    }

    private static DateTime NextSpecificDayOfMonth(DateTime from, string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return from.AddMonths(1);

        var days = data.Split(',')
            .Select(d => int.TryParse(d.Trim(), out var v) ? (int?)v : null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .OrderBy(x => x)
            .ToList();

        if (days.Count == 0) return from.AddMonths(1);

        var nextDay = days.FirstOrDefault(d => d > from.Day);
        if (nextDay > 0)
        {
            try { return new DateTime(from.Year, from.Month, nextDay, from.Hour, from.Minute, 0); }
            catch { /* day may not exist in this month */ }
        }

        var nextMonth = from.AddMonths(1);
        foreach (var d in days)
        {
            try { return new DateTime(nextMonth.Year, nextMonth.Month, d, from.Hour, from.Minute, 0); }
            catch { continue; }
        }

        return from.AddMonths(1);
    }

    private void ShowWindowsToastNotification(string noteTitle, DateTime remindAt)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            _logger.LogInformation("[TOAST] Hatirlatici: {Title} — {RemindAt:dd.MM.yyyy HH:mm}", noteTitle, remindAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not show Windows notification.");
        }
    }
}
