using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Tek bir alicinin gonderim sonucu — bir MailSendBatch'e bagli, status:
/// Sent / Failed / Queued. Hata varsa ErrorMessage doldurulur.
/// </summary>
[Description("Toplu mailin kisi-bazli gonderim log satiri (status, hata mesaji, alici adi/email/unvani, ne zaman gonderildi).")]
public sealed class MailSendLogItem
{
    public int Id { get; init; }

    /// <summary>FK -> MailSendBatch.Id (CASCADE).</summary>
    public int BatchId { get; init; }

    public int? ContactPersonId { get; init; }
    public string? RecipientName { get; init; }
    public required string RecipientEmail { get; init; }
    public string? TitleName { get; init; }
    public string? ContactName { get; init; }

    /// <summary>Sent / Failed / Queued.</summary>
    public required string Status { get; init; }

    public string? ErrorMessage { get; init; }
    public DateTime? SentAt { get; init; }
}
