using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// WhatsApp Web tarzi sohbet UI'i icin endpoint'ler:
///  GET  /Whatsapp                          → React/JSX sayfasi
///  GET  /Whatsapp/Conversations            → sohbet listesi (sidebar)
///  GET  /Whatsapp/Messages?phone=...       → bir sohbetin mesajlari
///  POST /Whatsapp/Send                     → metin mesaj gonder
///  POST /Whatsapp/MarkRead?phone=...       → bir sohbeti okundu isaretle
/// </summary>
public sealed class WhatsAppController : Controller
{
    private const int ConversationLimit = 200;
    private const int MessagesPerConversationLimit = 200;

    [HttpGet("/Whatsapp")]
    public IActionResult Index() => View();

    [HttpGet("/Whatsapp/Conversations")]
    public async Task<IActionResult> Conversations(
        [FromServices] IWaInboxRepository inbox, CancellationToken ct)
    {
        var convs = await inbox.GetConversationsAsync(ConversationLimit, ct);
        return Json(convs.Select(c => new
        {
            phone        = c.ContactPhone,
            contactId    = c.ContactId,
            displayName  = c.WaName ?? c.AccountTitle ?? c.ContactName ?? c.ContactPhone,
            accountCode  = c.AccountCode,
            lastBody     = c.LastBody,
            lastMedia    = c.LastMediaType,
            lastFromMe   = c.LastFromMe,
            // DB'den gelen DateTime Kind=Unspecified — JSON serileştirici 'Z' eklemiyor.
            // Tarayıcı UTC olarak yorumlasin diye Kind=Utc'yi acikca veriyoruz.
            lastAt       = DateTime.SpecifyKind(c.LastAt, DateTimeKind.Utc),
            unread       = c.UnreadCount,
        }));
    }

    [HttpGet("/Whatsapp/Messages")]
    public async Task<IActionResult> Messages(
        [FromServices] IWaInboxRepository inbox,
        [FromQuery] string phone,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest(new { error = "phone zorunlu" });

        var normalized = NormalizePhone(phone);
        var msgs = await inbox.GetMessagesByPhoneAsync(normalized, MessagesPerConversationLimit, ct);
        return Json(msgs.Select(m => new
        {
            id            = m.Id,
            direction     = m.Direction,            // 0 = gelen, 1 = giden
            body          = m.Body,
            mediaType     = m.MediaType,
            hasMedia      = m.HasMedia,
            mediaUrl      = m.MediaPath,            // /uploads/whatsapp/yyyy/MM/<id>.<ext>
            mediaMime     = m.MediaMime,
            mediaFileName = m.MediaFileName,
            mediaSize     = m.MediaSize,
            // DateTime Kind=Unspecified ise JSON 'Z' suffix'i eklenmez ve tarayici local sanir.
            // Database UTC tutuyor → Kind=Utc set ederek 'Z' suffix'ini garantile.
            at            = DateTime.SpecifyKind(m.ReceivedAt, DateTimeKind.Utc),
            readAt        = m.ReadAt.HasValue ? DateTime.SpecifyKind(m.ReadAt.Value, DateTimeKind.Utc) : (DateTime?)null,
        }));
    }

    [HttpPost("/Whatsapp/Send")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(
        [FromServices] IWhatsAppService whatsAppService,
        [FromBody] SendBody body,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Phone) || string.IsNullOrWhiteSpace(body.Text))
            return Json(new { success = false, message = "Telefon ve metin zorunlu." });

        // Chat UI'dan elle yazilan mesaj — interactive=true, anti-spam human-delay atlanir.
        var result = await whatsAppService.SendTextMessageAsync(body.Phone, body.Text, ct, interactive: true);
        return Json(new { success = result.Success, message = result.Message, messageId = result.MessageId });
    }

    /// <summary>
    /// Multipart upload: dosya + telefon + opsiyonel caption. Bridge'in /send-media ucuna proxy'ler.
    /// Composer'daki dosya butonu bunu cagiriyor.
    /// </summary>
    [HttpPost("/Whatsapp/SendMedia")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(60_000_000)] // 60 MB
    public async Task<IActionResult> SendMedia(
        [FromServices] CalibraHub.Application.Abstractions.Persistence.IWhatsAppConfigRepository configRepo,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromForm] string phone,
        [FromForm] string? caption,
        IFormFile file,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone) || file is null || file.Length == 0)
            return Json(new { success = false, message = "Telefon ve dosya zorunlu." });

        var cfg = await configRepo.GetAsync(ct);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            return Json(new { success = false, message = "Bridge URL ayarlanmamis." });

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            var req = new HttpRequestMessage(HttpMethod.Post,
                cfg.WebQrBridgeUrl.TrimEnd('/') + "/send-media");
            req.Headers.TryAddWithoutValidation("X-To", NormalizePhone(phone));
            // HTTP header'lari ASCII zorunlu — Turkce karakterler icin URL-encode et.
            // Bridge tarafinda decodeURIComponent ile geri cozulur.
            if (!string.IsNullOrWhiteSpace(caption))
                req.Headers.TryAddWithoutValidation("X-Caption", Uri.EscapeDataString(caption));
            if (!string.IsNullOrWhiteSpace(file.FileName))
                req.Headers.TryAddWithoutValidation("X-Filename", Uri.EscapeDataString(file.FileName));

            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                file.ContentType ?? "application/octet-stream");
            req.Content = content;

            using var resp = await client.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var o) && o.ValueKind == System.Text.Json.JsonValueKind.True;
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            var messageId = root.TryGetProperty("messageId", out var mid) ? mid.GetString() : null;
            return Json(new { success = ok, message = error ?? "Gonderildi.", messageId });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Bridge hatasi: {ex.Message}" });
        }
    }

    [HttpPost("/Whatsapp/MarkRead")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(
        [FromServices] IWaInboxRepository inbox,
        [FromQuery] string phone,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest(new { error = "phone zorunlu" });

        var normalized = NormalizePhone(phone);
        var updated = await inbox.MarkConversationReadAsync(normalized, DateTime.UtcNow, ct);
        return Json(new { success = true, updated });
    }

    [HttpPost("/Whatsapp/DeleteConversation")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConversation(
        [FromServices] IWaInboxRepository inbox,
        [FromQuery] string phone,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest(new { error = "phone zorunlu" });

        var normalized = NormalizePhone(phone);
        var deleted = await inbox.DeleteConversationAsync(normalized, ct);
        return Json(new { success = true, deleted });
    }

    public sealed class SendBody
    {
        public string? Phone { get; set; }
        public string? Text { get; set; }
    }

    private static string NormalizePhone(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input.Trim())
            if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}
