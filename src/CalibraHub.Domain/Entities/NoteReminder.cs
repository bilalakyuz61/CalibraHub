using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

public sealed class NoteReminder : Entity
{
    public Guid NoteId { get; init; }
    public required DateTime RemindAt { get; init; }
    public ReminderRecurrenceType RecurrenceType { get; init; } = ReminderRecurrenceType.None;
    public string? RecurrenceData { get; init; }

    /// <summary>Tetiklenince nereye gonderilecek. Default: InApp.</summary>
    public ReminderDeliveryChannel DeliveryChannel { get; init; } = ReminderDeliveryChannel.InApp;

    /// <summary>Hedef kullanici (NULL ise notun sahibine gider).</summary>
    public Guid? TargetUserId { get; init; }

    public bool IsSent { get; private set; }
    public DateTime? SentAt { get; private set; }

    public void MarkSent(DateTime sentAt)
    {
        IsSent = true;
        SentAt = sentAt;
    }
}
