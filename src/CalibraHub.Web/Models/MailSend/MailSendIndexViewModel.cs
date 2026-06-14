namespace CalibraHub.Web.Models.MailSend;

/// <summary>
/// MailSend index sayfasi (gonderim gecmisi C-Grid) icin viewmodel.
/// BoardConfig: SmartBoard fluent builder ile uretilen anonymous payload — Razor camelCase JSON serialize eder.
/// </summary>
public sealed class MailSendIndexViewModel
{
    public object? BoardConfig { get; init; }
}
