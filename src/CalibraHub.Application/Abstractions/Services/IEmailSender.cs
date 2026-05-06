namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Sirket SMTP profili ile mail gonderir; rapor ekleri dahil birden fazla alicisi olan
/// senaryolar icin uygundur. Reminder gonderimi icin <see cref="IReminderEmailSender"/>
/// bu servise delege edebilir.
/// </summary>
public interface IEmailSender
{
    Task<EmailResult> SendAsync(
        int companyId,
        IReadOnlyCollection<string> toEmails,
        string subject,
        string body,
        IReadOnlyCollection<EmailAttachment>? attachments,
        CancellationToken cancellationToken);
}

public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType);

public enum EmailStatus
{
    Sent = 0,
    Skipped = 1,
    Failed = 2,
}

public sealed record EmailResult(EmailStatus Status, string? Message)
{
    public static EmailResult Sent() => new(EmailStatus.Sent, null);
    public static EmailResult Skipped(string reason) => new(EmailStatus.Skipped, reason);
    public static EmailResult Failed(string reason) => new(EmailStatus.Failed, reason);
}
