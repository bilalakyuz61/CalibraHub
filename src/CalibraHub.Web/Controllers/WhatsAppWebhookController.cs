using System.Net.Http.Headers;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Security;
using CalibraHub.Application.WhatsApp;
using CalibraHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
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
    private const string GraphApiBase = "https://graph.facebook.com/v21.0";

    private readonly IWhatsAppConfigRepository _cfgRepo;
    private readonly IWaInboxRepository _inbox;
    private readonly ILogger<WhatsAppWebhookController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _env;

    public WhatsAppWebhookController(
        IWhatsAppConfigRepository cfgRepo,
        IWaInboxRepository inbox,
        ILogger<WhatsAppWebhookController> logger,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment env)
    {
        _cfgRepo           = cfgRepo;
        _inbox             = inbox;
        _logger            = logger;
        _httpClientFactory = httpClientFactory;
        _env               = env;
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

            // Cloud API medya indirme için cfg'yi bir kez çek
            var cfg = await _cfgRepo.GetAsync(ct);

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
                        await ProcessIncomingAsync(msg, defaultName, now, cfg, ct);
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

    private async Task ProcessIncomingAsync(JsonElement msg, string? defaultName, DateTime now,
        WhatsAppConfig? cfg, CancellationToken ct)
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
            string? cloudMediaId = null;

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
                        if (img.TryGetProperty("id", out var mid)) cloudMediaId = mid.GetString();
                    }
                    break;
                case "video":
                    mediaType = "video";
                    if (msg.TryGetProperty("video", out var vid))
                    {
                        if (vid.TryGetProperty("caption", out var c)) body = c.GetString();
                        if (vid.TryGetProperty("mime_type", out var mt)) mediaMime = mt.GetString();
                        if (vid.TryGetProperty("id", out var mid)) cloudMediaId = mid.GetString();
                    }
                    break;
                case "audio":
                case "voice":
                    mediaType = "audio";
                    if (msg.TryGetProperty("audio", out var aud))
                    {
                        if (aud.TryGetProperty("mime_type", out var mt)) mediaMime = mt.GetString();
                        if (aud.TryGetProperty("id", out var mid)) cloudMediaId = mid.GetString();
                    }
                    break;
                case "document":
                    mediaType = "document";
                    if (msg.TryGetProperty("document", out var docEl))
                    {
                        if (docEl.TryGetProperty("caption", out var c)) body = c.GetString();
                        if (docEl.TryGetProperty("mime_type", out var mt)) mediaMime = mt.GetString();
                        if (docEl.TryGetProperty("filename", out var fn)) mediaFileName = fn.GetString();
                        if (docEl.TryGetProperty("id", out var mid)) cloudMediaId = mid.GetString();
                    }
                    break;
                case "sticker":
                    mediaType = "sticker";
                    if (msg.TryGetProperty("sticker", out var st))
                    {
                        if (st.TryGetProperty("mime_type", out var mt)) mediaMime = mt.GetString();
                        if (st.TryGetProperty("id", out var mid)) cloudMediaId = mid.GetString();
                    }
                    break;
                case "location":
                    mediaType = "location";
                    break;
                default:
                    body = $"[Cloud API mesaj tipi: {type}]";
                    break;
            }

            // Cloud API medya indirme: media_id → Graph API URL → binary → disk
            string? mediaPath = null;
            int? mediaSize = null;
            if (!string.IsNullOrWhiteSpace(cloudMediaId) && cfg is not null
                && !string.IsNullOrEmpty(cfg.AccessTokenEncrypted))
            {
                (mediaPath, mediaSize, mediaMime) = await DownloadCloudMediaAsync(
                    cloudMediaId, msgId ?? "media", mediaMime, mediaFileName, cfg, now, ct);
            }

            await _inbox.InsertIfNotExistsAsync(new WaInboxMessage
            {
                BridgeMsgId   = msgId,
                Direction     = 0,
                ContactPhone  = NormalizePhone(from),
                ContactName   = defaultName,
                Body          = body,
                MediaType     = mediaType,
                HasMedia      = mediaType != "chat" && mediaType != "location",
                ReceivedAt    = receivedAt,
                CreatedAt     = now,
                MediaPath     = mediaPath,
                MediaMime     = mediaMime,
                MediaFileName = mediaFileName,
                MediaSize     = mediaSize,
            }, ct);

            _logger.LogInformation("[WaWebhook] received {type} from={from} id={id} media={hasMedia}",
                type, from, msgId, mediaPath is not null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WaWebhook] mesaj islenirken hata");
        }
    }

    private async Task<(string? path, int? size, string? mime)> DownloadCloudMediaAsync(
        string mediaId, string fileBaseName, string? knownMime, string? knownFileName,
        WhatsAppConfig cfg, DateTime now, CancellationToken ct)
    {
        try
        {
            var token = DpapiSecretDecryptor.DecryptIfNeeded(cfg.AccessTokenEncrypted!);
            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.Timeout = TimeSpan.FromSeconds(30);

            // Step 1: Media ID → download URL
            var metaResp = await client.GetAsync($"{GraphApiBase}/{mediaId}", ct);
            if (!metaResp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[WaWebhook] media meta {id} HTTP {code}", mediaId, (int)metaResp.StatusCode);
                return (null, null, knownMime);
            }

            using var metaDoc = JsonDocument.Parse(await metaResp.Content.ReadAsStringAsync(ct));
            var metaRoot = metaDoc.RootElement;
            var downloadUrl = metaRoot.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
            if (!string.IsNullOrWhiteSpace(metaRoot.TryGetProperty("mime_type", out var mimeEl) ? mimeEl.GetString() : null))
                knownMime = mimeEl.GetString();

            if (string.IsNullOrWhiteSpace(downloadUrl)) return (null, null, knownMime);

            // Step 2: İndir
            var fileResp = await client.GetAsync(downloadUrl, ct);
            if (!fileResp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[WaWebhook] media download {id} HTTP {code}", mediaId, (int)fileResp.StatusCode);
                return (null, null, knownMime);
            }

            var bytes = await fileResp.Content.ReadAsByteArrayAsync(ct);

            // Step 3: Diske kaydet
            var ext = knownFileName is not null
                ? Path.GetExtension(knownFileName)
                : MimeToExt(knownMime);
            var yyyy = now.Year.ToString("D4");
            var mm   = now.Month.ToString("D2");
            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "whatsapp", yyyy, mm);
            Directory.CreateDirectory(uploadsDir);
            var diskPath = Path.Combine(uploadsDir, $"{fileBaseName}{ext}");
            await System.IO.File.WriteAllBytesAsync(diskPath, bytes, ct);
            var urlPath = $"/uploads/whatsapp/{yyyy}/{mm}/{fileBaseName}{ext}";

            return (urlPath, bytes.Length, knownMime);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WaWebhook] medya indirilemedi: {id}", mediaId);
            return (null, null, knownMime);
        }
    }

    private static string MimeToExt(string? mime) => mime switch
    {
        "image/jpeg"           => ".jpg",
        "image/png"            => ".png",
        "image/webp"           => ".webp",
        "image/gif"            => ".gif",
        "video/mp4"            => ".mp4",
        "video/3gpp"           => ".3gp",
        "audio/ogg"            => ".ogg",
        "audio/mpeg"           => ".mp3",
        "audio/aac"            => ".aac",
        "audio/opus"           => ".opus",
        "application/pdf"      => ".pdf",
        "application/zip"      => ".zip",
        _                      => "",
    };

    private static string NormalizePhone(string input)
        => WaPhoneNormalizer.Normalize(input) ?? string.Empty;
}
