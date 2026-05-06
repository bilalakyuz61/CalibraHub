using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;

namespace CalibraHub.Infrastructure.Notifications;

/// <summary>
/// Generic SMTP gonderici — sirketin aktif profili ile maili, istenirse dosya ekleriyle
/// birlikte gonderir. ReminderEmailSender ile ayni pattern.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly ISmtpProfileRepository _smtpRepo;

    public SmtpEmailSender(ISmtpProfileRepository smtpRepo)
    {
        _smtpRepo = smtpRepo;
    }

    public async Task<EmailResult> SendAsync(
        int companyId,
        IReadOnlyCollection<string> toEmails,
        string subject,
        string body,
        IReadOnlyCollection<EmailAttachment>? attachments,
        CancellationToken cancellationToken)
    {
        var validRecipients = toEmails
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (validRecipients.Count == 0)
            return EmailResult.Skipped("Alici listesi bos.");

        var profiles = await _smtpRepo.GetAllAsync(cancellationToken);
        // companyId <= 0 — scheduler bagimsiz cagri; fallback olarak herhangi bir aktif profil
        var profile = companyId > 0
            ? profiles.FirstOrDefault(p => p.CompanyId == companyId && p.IsActive)
              ?? profiles.FirstOrDefault(p => p.IsActive)
            : profiles.FirstOrDefault(p => p.IsActive);
        if (profile is null)
            return EmailResult.Skipped("Aktif SMTP profili yok — Admin > Sirket Ayarlari > SMTP altindan ekleyin.");

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(profile.FromEmail,
                    string.IsNullOrWhiteSpace(profile.FromDisplayName) ? profile.FromEmail : profile.FromDisplayName),
                Subject = subject,
                Body = body,
                IsBodyHtml = false,
            };
            foreach (var to in validRecipients)
            {
                try { message.To.Add(to); }
                catch { /* skip malformed */ }
            }
            if (message.To.Count == 0)
                return EmailResult.Skipped("Gecerli alici adresi yok.");

            if (attachments is not null)
            {
                foreach (var att in attachments)
                {
                    var stream = new MemoryStream(att.Content);
                    var ct = new ContentType(string.IsNullOrWhiteSpace(att.ContentType)
                        ? "application/octet-stream"
                        : att.ContentType);
                    message.Attachments.Add(new Attachment(stream, att.FileName, ct.MediaType));
                }
            }

            using var client = new SmtpClient(profile.Host, profile.Port)
            {
                EnableSsl = profile.UseSsl,
                Credentials = string.IsNullOrWhiteSpace(profile.Username)
                    ? null
                    : new NetworkCredential(profile.Username, profile.Password),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 30000,
            };

            await client.SendMailAsync(message, cancellationToken);
            return EmailResult.Sent();
        }
        catch (SmtpException ex)
        {
            return EmailResult.Failed("SMTP: " + ex.Message);
        }
        catch (Exception ex)
        {
            return EmailResult.Failed(ex.Message);
        }
    }
}
