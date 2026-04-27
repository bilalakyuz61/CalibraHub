using System.Net;
using System.Net.Mail;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;

namespace CalibraHub.Infrastructure.Notifications;

/// <summary>
/// AdminManagementService.SendMailAsync pattern'ini yeniden kullanir — sirketin aktif SMTP profili
/// ile hatirlatici mailini gonderir. Authentication yok ise credential eklemez; OAuth2 su an
/// desteklenmiyor (Gmail/Outlook icin Password/AppPassword yeterli).
/// </summary>
public sealed class SmtpReminderEmailSender : IReminderEmailSender
{
    private readonly ISmtpProfileRepository _smtpRepo;

    public SmtpReminderEmailSender(ISmtpProfileRepository smtpRepo)
    {
        _smtpRepo = smtpRepo;
    }

    public async Task<ReminderEmailResult> SendAsync(
        int companyId,
        string toEmail,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            return ReminderEmailResult.Skipped("Alici mail adresi bos.");

        var profiles = await _smtpRepo.GetAllAsync(cancellationToken);
        var profile = profiles.FirstOrDefault(p => p.CompanyId == companyId && p.IsActive);
        if (profile is null)
            return ReminderEmailResult.Skipped("Sirkette aktif SMTP profili yok.");

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
            message.To.Add(toEmail);

            using var client = new SmtpClient(profile.Host, profile.Port)
            {
                EnableSsl = profile.UseSsl,
                Credentials = string.IsNullOrWhiteSpace(profile.Username)
                    ? null
                    : new NetworkCredential(profile.Username, profile.Password),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15000,
            };

            await client.SendMailAsync(message, cancellationToken);
            return ReminderEmailResult.Sent();
        }
        catch (SmtpException ex)
        {
            return ReminderEmailResult.Failed("SMTP: " + ex.Message);
        }
        catch (Exception ex)
        {
            return ReminderEmailResult.Failed(ex.Message);
        }
    }
}
