namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Hatirlatici bildirimleri icin e-posta gonderici. Sirketin aktif SMTP profili
/// ile mail atar; profil yoksa <see cref="ReminderEmailResult.Skipped"/> doner.
/// </summary>
public interface IReminderEmailSender
{
    Task<ReminderEmailResult> SendAsync(
        int companyId,
        string toEmail,
        string subject,
        string body,
        CancellationToken cancellationToken);
}

public enum ReminderEmailStatus
{
    Sent = 0,
    Skipped = 1, // SMTP profili yok / alici mail adresi bos
    Failed = 2,
}

public sealed record ReminderEmailResult(ReminderEmailStatus Status, string? Message)
{
    public static ReminderEmailResult Sent() => new(ReminderEmailStatus.Sent, null);
    public static ReminderEmailResult Skipped(string reason) => new(ReminderEmailStatus.Skipped, reason);
    public static ReminderEmailResult Failed(string reason) => new(ReminderEmailStatus.Failed, reason);
}
