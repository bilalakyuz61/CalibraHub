using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Worker;

public sealed class ReminderNotificationWorker : BackgroundService
{
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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing due reminders.");
            }

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

    private async Task ProcessDueRemindersAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var noteRepository = scope.ServiceProvider.GetRequiredService<INoteRepository>();

        var dueReminders = await noteRepository.GetUnsentDueRemindersAsync(cancellationToken);
        if (dueReminders.Count == 0) return;

        _logger.LogInformation("Processing {Count} due reminder(s).", dueReminders.Count);

        foreach (var (reminder, note) in dueReminders)
        {
            try
            {
                ShowWindowsToastNotification(note.Title, reminder.RemindAt);
                await noteRepository.MarkReminderSentAsync(reminder.Id, DateTime.Now, cancellationToken);

                if (reminder.RecurrenceType != ReminderRecurrenceType.None)
                {
                    var nextRemindAt = ComputeNextRemindAt(reminder.RemindAt, reminder.RecurrenceType, reminder.RecurrenceData);
                    if (nextRemindAt.HasValue)
                    {
                        var nextReminder = new NoteReminder
                        {
                            NoteId = reminder.NoteId,
                            RemindAt = nextRemindAt.Value,
                            RecurrenceType = reminder.RecurrenceType,
                            RecurrenceData = reminder.RecurrenceData
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
