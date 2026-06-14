namespace CalibraHub.Web.Models.MailSend;

/// <summary>
/// 2026-05-30 — MailSend Detail sayfasi (Compose ile ayni 3-sekmeli wizard, read-only).
/// SmartCard tiklamasi /MailSend/Detail?batchId=X uzerinden buraya navigate eder.
/// </summary>
public sealed class MailSendDetailViewModel
{
    public int BatchId { get; init; }
    public string? LayoutName { get; init; }
    public string? LayoutDescription { get; init; }
    public string? Subject { get; init; }
    public string? BodyPreview { get; init; }
    public int TotalCount { get; init; }
    public int SentCount { get; init; }
    public int FailCount { get; init; }
    public string? SentBy { get; init; }
    public System.DateTime SentAt { get; init; }
    public string[] TitleNames { get; init; } = System.Array.Empty<string>();
    public string? PreviewHtml { get; init; }
    public RecipientRow[] Items { get; init; } = System.Array.Empty<RecipientRow>();

    public sealed record RecipientRow(
        int Id,
        string? RecipientName,
        string? RecipientEmail,
        string? TitleName,
        string? ContactName,
        string? Status,
        string? ErrorMessage,
        System.DateTime? SentAt);
}
