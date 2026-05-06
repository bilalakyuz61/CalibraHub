using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Meta Cloud API webhook receiver — Meta'nin Webhook olarak cagiracagi endpoint.
///
/// Meta App config: Webhook URL = https://your-domain.com/api/whatsapp/webhook
///                  Verify Token = WhatsAppConfig.WebhookVerifyToken
///                  Subscribe = "messages" (gelen mesaj olaylari icin)
///
/// Endpoint'ler:
///  GET  /api/whatsapp/webhook  — Meta verify token challenge
///  POST /api/whatsapp/webhook  — Meta'nin gelen mesaj/status payload'larini gonderdigi URL
///
/// Bridge polling ile ayni inbox tablosuna yazar (provider farketmez, hibrit).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/whatsapp/webhook")]
public sealed class WhatsAppWebhookController : ControllerBase
{
    private readonly IWhatsAppConfigRepository _cfgRepo;
    private readonly IWaInboxRepository _inbox;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        IWhatsAppConfigRepository cfgRepo,
        IWaInboxRepository inbox,
        ILogger<WhatsAppWebhookController> logger)
    {
        _cfgRepo = cfgRepo;
        _inbox = inbox;
        _logger = logger;
    }

    /// <summary>
    /// Meta verify GET: Meta App config'inde Webhook URL girilince Meta bu endpoint'e
    /// hub.mode=subscribe + hub.challenge + hub.verify_token gonderir. Token uyusursa
    /// challenge text'ini geri gonderiyoruz.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        CancellationToken ct)
    {
        var cfg = await _cfgRepo.GetAsync(ct);
        var expectedToken = cfg?.WebhookVerifyToken;

        if (mode == "subscribe"
            && !string.IsNullOrEmpty(expectedToken)
            && string.Equals(token, expectedToken, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(challenge))
        {
            _logger.LogInformation("[WaWebhook] verify OK");
            return Content(challenge, "text/plain");
        }

        _logger.LogWarning("[WaWebhook] verify FAILED — mode={mode} token-match={tokenMatch}",
            mode, token == expectedToken);
        return Forbid();
    }

    /// <summary>
    /// Meta'nin gonderdigi gelen mesaj payload'i. Yapi:
    ///   { entry: [{ changes: [{ value: { messages: [{ from, id, timestamp, type, text:{body}, image:{...}, ... }] } }] }] }
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // Body'i okumak icin
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var raw = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(raw)) return Ok();

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // entry[].changes[].value.messages[]
            if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return Ok();

            var now = DateTime.UtcNow;
            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value)) continue;
                    if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
                        continue;

                    // Contact name lookup (varsa)
                    string? defaultName = null;
                    if (value.TryGetProperty("contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array)
                    {
                        var first = contacts.EnumerateArray().FirstOrDefault();
                        if (first.ValueKind == JsonValueKind.Object
                            && first.TryGetProperty("profile", out var prof)
                            && prof.TryGetProperty("name", out var n))
                            defaultName = n.GetString();
                    }

                    foreach (var msg in messages.EnumerateArray())
                    {
                        await ProcessIncomingAsync(msg, defaultName, now, ct);
                    }
                }
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WaWebhook] payload parse hatasi");
            return Ok(); // Meta retry yapmasin diye OK don
        }
    }

    private async Task ProcessIncomingAsync(JsonElement msg, string? defaultName, DateTime now, CancellationToken ct)
    {
        try
        {
            var msgId = msg.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var from = msg.TryGetProperty("from", out var fromEl) ? fromEl.GetString() : null;
            var type = msg.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "text";

            if (string.IsNullOrWhiteSpace(from)) return;

            long ts = 0;
            if (msg.TryGetProperty("timestamp", out var tsEl))
            {
                var s = tsEl.GetString();
                long.TryParse(s, out ts);
            }
            var receivedAt = ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime : now;

            string? body = null;
            string mediaType = "chat";
            string? mediaMime = null;
            string? mediaFileName = null;

            switch (type)
            {
                case "text":
                    if (msg.TryGetProperty("text", out var textEl) && textEl.TryGetProperty("body", out var b))
                        body = b.GetString();
                    break;
                case "image":
                    mediaType = "image";
                    if (msg.TryGetProperty("image", out var img))
                    {
                        if (img.TryGetProperty("caption", out var c)) body = c.GetString();
                        if (img.TryGetProperty("mime_type", out var mt)) mediaMime = mt.GetString();
                    }
                    break;
                case "video":
                    mediaType = "video";
                    if (msg.TryGetProperty("video", out var vid))
                    {
                        if (vid.TryGetProperty("caption", out var c)) body = c.GetString();
                        if (vid.TryGetProperty("mime_type", out var mt)) mediaMime = mt.GetString();
                    }
                    break;
                case "audio":
                case "voice":
                    mediaType = "audio";
                    if (msg.TryGetProperty("audio", out var aud))
                    {
                        if (aud.TryGetProperty("mime_type", out var mt)) mediaMime = mt.GetString();
                    }
                    break;
                case "document":
                    mediaType = "document";
                    if (msg.TryGetProperty("document", out var docEl))
                    {
                        if (docEl.TryGetProperty("caption", out var c)) body = c.GetString();
                        if (docEl.TryGetProperty("mime_type", out var mt)) mediaMime = mt.GetString();
                        if (docEl.TryGetProperty("filename", out var fn)) mediaFileName = fn.GetString();
                    }
                    break;
                case "sticker":
                    mediaType = "sticker";
                    if (msg.TryGetProperty("sticker", out var st))
                    {
                        if (st.TryGetProperty("mime_type", out var mt)) mediaMime = mt.GetString();
                    }
                    break;
                case "location":
                    mediaType = "location";
                    break;
                default:
                    body = $"[Cloud API mesaj tipi: {type}]";
                    break;
            }

            // Cloud API'da medya direkt URL gondermez — sadece media_id verir.
            // Tam medya indirme icin Graph API'ya extra istek lazim. Simdilik metadata kayit edelim,
            // medya bytelarini cekme isi sonraki adim olabilir.
            await _inbox.InsertIfNotExistsAsync(new WaInboxMessage
            {
                BridgeMsgId   = msgId,
                Direction     = 0,                 // gelen
                ContactPhone  = NormalizePhone(from),
                ContactName   = defaultName,
                Body          = body,
                MediaType     = mediaType,
                HasMedia      = mediaType != "chat" && mediaType != "location",
                ReceivedAt    = receivedAt,
                CreatedAt     = now,
                MediaPath     = null,              // Cloud API: medya bytes ayri Graph API call ile cekilir (TODO)
                MediaMime     = mediaMime,
                MediaFileName = mediaFileName,
                MediaSize     = null,
            }, ct);

            _logger.LogInformation("[WaWebhook] received {type} from={from} id={id}", type, from, msgId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WaWebhook] mesaj islenirken hata");
        }
    }

    private static string NormalizePhone(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input.Trim())
            if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}
