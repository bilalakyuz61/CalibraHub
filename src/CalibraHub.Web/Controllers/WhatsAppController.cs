using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.WhatsApp;
using CalibraHub.Web.Authorization;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// WhatsApp Web tarzi sohbet UI'i icin endpoint'ler:
///  GET  /Whatsapp                          → React/JSX sayfasi (VIEW)
///  GET  /Whatsapp/Conversations            → sohbet listesi (VIEW)
///  POST /Whatsapp/Send                     → mesaj gönder (CREATE → Mesaj Gönderme)
///  POST /Whatsapp/Delete*                  → mesaj sil (DELETE_OWN → Mesaj Silme)
///  POST /Whatsapp/MarkRead                 → okundu işareti — operasyonel, ek izin gerektirmez
/// </summary>
[PermissionScope(FormCodes.WhatsApp)]
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
        return Json(convs.Select(c =>
        {
            // LID kontağı resolve edilememişse ham LID yerine "Bilinmeyen kişi" göster.
            // Heuristic: is_lid flag henüz set edilmemişse 15+ basamaklı numara = muhtemelen LID.
            var isLidContact = c.IsLid
                || (c.ContactPhone.Length >= 15
                    && c.WaName is null
                    && c.AccountTitle is null
                    && c.ContactName is null);

            string displayName;
            if (isLidContact && c.WaName is null && c.AccountTitle is null && c.ContactName is null)
            {
                var shortLid = c.ContactPhone.Length > 6
                    ? "…" + c.ContactPhone[^4..]
                    : c.ContactPhone;
                displayName = $"Bilinmeyen kişi ({shortLid})";
            }
            else
            {
                displayName = c.WaName ?? c.AccountTitle ?? c.ContactName ?? c.ContactPhone;
            }

            return new
            {
                phone        = c.ContactPhone,
                contactId    = c.ContactId,
                displayName,
                accountCode  = c.AccountCode,
                lastBody     = c.LastBody,
                lastMedia    = c.LastMediaType,
                lastFromMe   = c.LastFromMe,
                // DB'den gelen DateTime Kind=Unspecified — JSON serileştirici 'Z' eklemiyor.
                // Tarayıcı UTC olarak yorumlasin diye Kind=Utc'yi acikca veriyoruz.
                lastAt          = DateTime.SpecifyKind(c.LastAt, DateTimeKind.Utc),
                unread          = c.UnreadCount,
                isLid           = isLidContact,
                // Faz 4: grup alanları
                isGroup         = c.IsGroup,
                groupJid        = c.GroupJid,
                groupSubject    = c.GroupSubject,
                memberCount     = c.GroupMemberCount,
            };
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
            bridgeMsgId   = m.BridgeMsgId,
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
            // Faz 3
            quotedMsgId   = m.QuotedMsgId,
            reactionEmoji = m.ReactionEmoji,
            isDeleted     = m.IsDeleted,
            deliveryStatus= m.DeliveryStatus,
            // Faz 4: grup alanları
            groupJid      = m.GroupJid,
            senderJid     = m.SenderJid,
            senderName    = m.SenderName,
        }));
    }

    [HttpPost("/Whatsapp/Send")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(
        [FromServices] IWhatsAppService whatsAppService,
        [FromServices] IWaInboxRepository inbox,
        [FromServices] IWhatsAppRealTimeNotifier notifier,
        [FromBody] SendBody body,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Phone) || string.IsNullOrWhiteSpace(body.Text))
            return Json(new { success = false, message = "Telefon ve metin zorunlu." });

        // Chat UI'dan elle yazilan mesaj — interactive=true, anti-spam human-delay atlanir.
        var result = await whatsAppService.SendTextMessageAsync(body.Phone, body.Text, ct, interactive: true);

        if (result.Success)
        {
            // Gönderilen mesajı wa_inbox'a ekle — UI'ın beklemesine gerek kalmaz.
            // Web QR (bridge) path'ında bridge zaten echo'luyor → InsertIfNotExists dedup'ı UNIQUE'e takılır, sorun yok.
            // Cloud API path'ında echo olmadığı için bu insert tek kayıttır.
            var normalized = NormalizePhone(body.Phone);
            var msgId = result.MessageId ?? $"local-{DateTime.UtcNow.Ticks}";
            var now = DateTime.UtcNow;
            try
            {
                await inbox.InsertIfNotExistsAsync(new Domain.Entities.WaInboxMessage
                {
                    BridgeMsgId  = msgId,
                    Direction    = 1,
                    ContactPhone = normalized,
                    Body         = body.Text,
                    MediaType    = "chat",
                    HasMedia     = false,
                    ReceivedAt   = now,
                    CreatedAt    = now,
                }, ct);

                await notifier.MessageReceivedAsync(normalized, msgId, 1, body.Text, "chat", false, null, null, null, null, now, ct);
                await notifier.ConversationUpdatedAsync(normalized, ct);
            }
            catch { /* SignalR/insert hataları mesaj gönderimi engellememeli */ }
        }

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
        [FromServices] IWaInboxRepository inbox,
        [FromServices] IWhatsAppRealTimeNotifier notifier,
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
            req.Headers.TryAddWithoutValidation("X-To", SafePhone(phone));
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

            if (ok)
            {
                // Gönderilen medyayı diske kaydet + wa_inbox'a ekle.
                // Polling service'in gelen medya için kullandığı aynı pattern.
                var normalized = NormalizePhone(phone);
                var now = DateTime.UtcNow;
                var msgId = messageId ?? $"local-{now.Ticks}";
                var mime = file.ContentType ?? "application/octet-stream";
                var mediaType = mime.Split('/')[0] switch
                {
                    "image" => "image",
                    "video" => "video",
                    "audio" => "audio",
                    _       => "document",
                };
                var extRaw = Path.GetExtension(file.FileName ?? string.Empty).TrimStart('.');
                var ext = !string.IsNullOrEmpty(extRaw)
                    ? extRaw.ToLowerInvariant()
                    : mime switch
                    {
                        "image/jpeg"      => "jpg",
                        "image/png"       => "png",
                        "image/gif"       => "gif",
                        "image/webp"      => "webp",
                        "video/mp4"       => "mp4",
                        "audio/ogg"       => "ogg",
                        "audio/mpeg"      => "mp3",
                        "application/pdf" => "pdf",
                        _                 => "bin",
                    };

                // Dosyayı kaydet — wwwroot/uploads/whatsapp/yyyy/MM/<msgId>.<ext>
                var safeId = string.Concat(msgId.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
                if (string.IsNullOrEmpty(safeId)) safeId = Guid.NewGuid().ToString("N");
                var wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var subPath = Path.Combine("uploads", "whatsapp", now.Year.ToString(), now.Month.ToString("D2"));
                var dir = Path.Combine(wwwroot, subPath);
                Directory.CreateDirectory(dir);
                var fullPath = Path.Combine(dir, $"{safeId}.{ext}");
                await System.IO.File.WriteAllBytesAsync(fullPath, bytes, ct);
                var urlPath = "/" + subPath.Replace('\\', '/') + "/" + safeId + "." + ext;

                try
                {
                    await inbox.InsertIfNotExistsAsync(new Domain.Entities.WaInboxMessage
                    {
                        BridgeMsgId   = msgId,
                        Direction     = 1,
                        ContactPhone  = normalized,
                        Body          = caption,
                        MediaType     = mediaType,
                        HasMedia      = true,
                        MediaPath     = urlPath,
                        MediaMime     = mime,
                        MediaFileName = file.FileName,
                        MediaSize     = bytes.Length,
                        ReceivedAt    = now,
                        CreatedAt     = now,
                        DeliveryStatus = "sent",
                    }, ct);

                    await notifier.MessageReceivedAsync(normalized, msgId, 1, caption, mediaType, true, urlPath, mime, file.FileName, bytes.Length, now, ct);
                    await notifier.ConversationUpdatedAsync(normalized, ct);
                }
                catch { /* insert/SignalR hataları gönderimi engellememeli */ }
            }

            return Json(new { success = ok, message = error ?? "Gonderildi.", messageId });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Bridge hatasi: {"Islem sirasinda bir hata olustu."}" });
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

    /// <summary>
    /// Karşı tarafa "yazıyor…" veya "durdu" sinyali gönderir.
    /// Bridge'in /send-typing ucuna proxy'ler.
    /// </summary>
    [HttpPost("/Whatsapp/SendTyping")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTyping(
        [FromServices] CalibraHub.Application.Abstractions.Persistence.IWhatsAppConfigRepository configRepo,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromQuery] string phone,
        [FromQuery] bool isTyping,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return Json(new { success = false });

        var cfg = await configRepo.GetAsync(ct);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            return Json(new { success = false });

        try
        {
            var normalized = SafePhone(phone);
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var resp = await client.PostAsJsonAsync(
                cfg.WebQrBridgeUrl.TrimEnd('/') + "/send-typing",
                new { phone = normalized, isTyping }, ct);
            return Json(new { success = resp.IsSuccessStatusCode });
        }
        catch { return Json(new { success = false }); }
    }

    /// <summary>
    /// Karşı tarafa "okundu" bildirimi gönderir (mavi tik).
    /// Bridge'in /send-read-receipt ucuna proxy'ler.
    /// </summary>
    [HttpPost("/Whatsapp/SendReadReceipt")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendReadReceipt(
        [FromServices] CalibraHub.Application.Abstractions.Persistence.IWhatsAppConfigRepository configRepo,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromQuery] string phone,
        [FromBody] ReadReceiptBody body,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone) || body?.MessageIds is null || body.MessageIds.Count == 0)
            return Json(new { success = false });

        var cfg = await configRepo.GetAsync(ct);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            return Json(new { success = false });

        try
        {
            var normalized = NormalizePhone(phone);
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var resp = await client.PostAsJsonAsync(
                cfg.WebQrBridgeUrl.TrimEnd('/') + "/send-read-receipt",
                new { phone = normalized, messageIds = body.MessageIds }, ct);
            return Json(new { success = resp.IsSuccessStatusCode });
        }
        catch { return Json(new { success = false }); }
    }

    public sealed class ReadReceiptBody
    {
        public List<string>? MessageIds { get; set; }
    }

    /// <summary>Alıntılı yanıt mesajı gönderir (quoted reply).</summary>
    [HttpPost("/Whatsapp/SendReply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendReply(
        [FromServices] IWhatsAppService whatsAppService,
        [FromServices] CalibraHub.Application.Abstractions.Persistence.IWhatsAppConfigRepository configRepo,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromBody] SendReplyBody body,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Phone) || string.IsNullOrWhiteSpace(body.Text))
            return Json(new { success = false, message = "Phone, text zorunlu." });

        var cfg = await configRepo.GetAsync(ct);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            return Json(new { success = false, message = "Bridge URL ayarlanmamış." });

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var resp = await client.PostAsJsonAsync(cfg.WebQrBridgeUrl.TrimEnd('/') + "/send-reply", new
            {
                phone         = SafePhone(body.Phone),
                text          = body.Text,
                quotedId      = body.QuotedId,
                quotedBody    = body.QuotedBody,
                quotedFromMe  = body.QuotedFromMe,
            }, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var ok = doc.RootElement.TryGetProperty("ok", out var o) && o.ValueKind == System.Text.Json.JsonValueKind.True;
            var msgId = doc.RootElement.TryGetProperty("messageId", out var mid) ? mid.GetString() : null;
            return Json(new { success = ok, messageId = msgId });
        }
        catch (Exception ex) { return Json(new { success = false, message = "Islem sirasinda bir hata olustu." }); }
    }

    /// <summary>Mesaja emoji reaksiyonu gönderir. Boş emoji = reaksiyonu kaldır.</summary>
    [HttpPost("/Whatsapp/SendReaction")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendReaction(
        [FromServices] CalibraHub.Application.Abstractions.Persistence.IWhatsAppConfigRepository configRepo,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IWaInboxRepository inbox,
        [FromBody] SendReactionBody body,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Phone) || string.IsNullOrWhiteSpace(body.MessageId))
            return Json(new { success = false });

        var cfg = await configRepo.GetAsync(ct);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            return Json(new { success = false, message = "Bridge URL ayarlanmamış." });

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var resp = await client.PostAsJsonAsync(cfg.WebQrBridgeUrl.TrimEnd('/') + "/send-reaction", new
            {
                phone     = SafePhone(body.Phone),
                messageId = body.MessageId,
                emoji     = body.Emoji ?? "",
                fromMe    = body.FromMe,
            }, ct);

            if (resp.IsSuccessStatusCode)
                await inbox.UpdateReactionAsync(body.MessageId, string.IsNullOrWhiteSpace(body.Emoji) ? null : body.Emoji, ct);

            return Json(new { success = resp.IsSuccessStatusCode });
        }
        catch (Exception ex) { return Json(new { success = false, message = "Islem sirasinda bir hata olustu." }); }
    }

    /// <summary>Mesajı her iki yönde siler. DB'de is_deleted=1 yapılır.</summary>
    [HttpPost("/Whatsapp/DeleteMessage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMessage(
        [FromServices] CalibraHub.Application.Abstractions.Persistence.IWhatsAppConfigRepository configRepo,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IWaInboxRepository inbox,
        [FromBody] DeleteMessageBody body,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Phone) || string.IsNullOrWhiteSpace(body.MessageId))
            return Json(new { success = false });

        var cfg = await configRepo.GetAsync(ct);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            return Json(new { success = false, message = "Bridge URL ayarlanmamış." });

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            await client.PostAsJsonAsync(cfg.WebQrBridgeUrl.TrimEnd('/') + "/delete-message", new
            {
                phone     = SafePhone(body.Phone),
                messageId = body.MessageId,
                fromMe    = body.FromMe,
            }, ct);

            await inbox.MarkDeletedAsync(body.MessageId, ct);
            return Json(new { success = true });
        }
        catch (Exception ex) { return Json(new { success = false, message = "Islem sirasinda bir hata olustu." }); }
    }

    /// <summary>Sohbet içi metin arama.</summary>
    [HttpGet("/Whatsapp/SearchMessages")]
    public async Task<IActionResult> SearchMessages(
        [FromServices] IWaInboxRepository inbox,
        [FromQuery] string phone,
        [FromQuery] string q,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Json(Array.Empty<object>());

        var results = await inbox.SearchMessagesAsync(NormalizePhone(phone), q, 50, ct);
        return Json(results.Select(m => new
        {
            id          = m.Id,
            bridgeMsgId = m.BridgeMsgId,
            direction   = m.Direction,
            body        = m.Body,
            at          = DateTime.SpecifyKind(m.ReceivedAt, DateTimeKind.Utc),
        }));
    }

    public sealed class SendReplyBody
    {
        public string? Phone { get; set; }
        public string? Text { get; set; }
        public string? QuotedId { get; set; }
        public string? QuotedBody { get; set; }
        public bool QuotedFromMe { get; set; }
    }

    public sealed class SendReactionBody
    {
        public string? Phone { get; set; }
        public string? MessageId { get; set; }
        public string? Emoji { get; set; }
        public bool FromMe { get; set; }
    }

    public sealed class DeleteMessageBody
    {
        public string? Phone { get; set; }
        public string? MessageId { get; set; }
        public bool FromMe { get; set; }
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

        var normalized = SafePhone(phone);
        var deleted = await inbox.DeleteConversationAsync(normalized, ct);
        return Json(new { success = true, deleted });
    }

    public sealed class SendBody
    {
        public string? Phone { get; set; }
        public string? Text { get; set; }
    }

    // ── Faz 4: Grup endpoint'leri ──────────────────────────────────────────

    /// <summary>Tüm grupları bridge'den çekip DB'ye senkronize et, listesini döndür.</summary>
    [HttpGet("/Whatsapp/Groups")]
    public async Task<IActionResult> Groups(
        [FromServices] IWaGroupRepository groupRepo,
        [FromServices] IWhatsAppService waService,
        CancellationToken ct)
    {
        var groups = await groupRepo.GetAllAsync(ct);
        return Json(groups.Select(g => new
        {
            jid         = g.GroupJid,
            subject     = g.Subject,
            description = g.Description,
            memberCount = g.MemberCount,
        }));
    }

    /// <summary>Bridge'den grup listesini çek ve DB'ye upsert et.</summary>
    [HttpPost("/Whatsapp/SyncGroups")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncGroups(
        [FromServices] IWaGroupRepository groupRepo,
        [FromServices] IWhatsAppConfigRepository configRepo,
        CancellationToken ct)
    {
        var cfg = await configRepo.GetAsync(ct);
        if (cfg is null || !cfg.IsEnabled || string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            return Json(new { ok = false, error = "Bridge yapılandırılmamış" });

        var bridgeBase = cfg.WebQrBridgeUrl.TrimEnd('/');
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var resp = await http.GetFromJsonAsync<BridgeGroupsResponse>($"{bridgeBase}/groups", ct);
            if (resp?.Groups is null) return Json(new { ok = false, error = "Bridge yanıt vermedi" });

            foreach (var g in resp.Groups)
            {
                if (string.IsNullOrWhiteSpace(g.Jid)) continue;
                var group = await groupRepo.GetOrCreateAsync(g.Jid, g.Subject ?? g.Jid, ct);
                if (g.Participants?.Count > 0)
                {
                    var members = g.Participants
                        .Where(p => !string.IsNullOrWhiteSpace(p.Jid))
                        .Select(p => new WaGroupMemberInput(p.Jid!, p.Name, p.Role ?? "member"))
                        .ToList();
                    await groupRepo.UpsertMembersAsync(group.Id, members, ct);
                }
            }
            return Json(new { ok = true, count = resp.Groups.Count });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>Bir grubun üyelerini döndür.</summary>
    [HttpGet("/Whatsapp/GroupMembers")]
    public async Task<IActionResult> GroupMembers(
        [FromServices] IWaGroupRepository groupRepo,
        [FromQuery] string groupJid,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(groupJid)) return BadRequest();
        var group = await groupRepo.GetByJidAsync(groupJid, ct);
        if (group is null) return Json(Array.Empty<object>());
        var members = await groupRepo.GetMembersAsync(group.Id, ct);
        return Json(members.Select(m => new
        {
            jid       = m.Jid,
            name      = m.Name,
            role      = m.Role,
            joinedAt  = DateTime.SpecifyKind(m.JoinedAt, DateTimeKind.Utc),
        }));
    }

    private sealed class BridgeGroupsResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("groups")]
        public List<BridgeGroup>? Groups { get; set; }
    }

    private sealed class BridgeGroup
    {
        [System.Text.Json.Serialization.JsonPropertyName("jid")]
        public string? Jid { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("subject")]
        public string? Subject { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("description")]
        public string? Description { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("participants")]
        public List<BridgeGroupParticipant>? Participants { get; set; }
    }

    private sealed class BridgeGroupParticipant
    {
        [System.Text.Json.Serialization.JsonPropertyName("jid")]
        public string? Jid { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("role")]
        public string? Role { get; set; }
    }

    // ── Avatar (profil fotoğrafı) proxy — bridge /avatar, 6 saat in-memory cache ──
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string? Url, DateTime Ts)>
        AvatarCache = new();
    private static readonly TimeSpan AvatarTtl = TimeSpan.FromHours(6);

    /// <summary>Kişinin WhatsApp profil fotoğrafı URL'ini döndürür (bridge üzerinden, cache'li).</summary>
    [HttpGet("/Whatsapp/Avatar")]
    public async Task<IActionResult> Avatar(
        [FromServices] IWhatsAppConfigRepository configRepo,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromQuery] string phone,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone)) return Json(new { url = (string?)null });

        var key = SafePhone(phone);
        if (AvatarCache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.Ts < AvatarTtl)
            return Json(new { url = cached.Url });

        var cfg = await configRepo.GetAsync(ct);
        if (cfg is null || !cfg.IsEnabled || string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            return Json(new { url = (string?)null });

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var resp = await client.GetAsync(
                cfg.WebQrBridgeUrl.TrimEnd('/') + "/avatar?phone=" + Uri.EscapeDataString(key), ct);
            string? url = null;
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
                url = json.TryGetProperty("url", out var u) && u.ValueKind == System.Text.Json.JsonValueKind.String
                    ? u.GetString() : null;
            }
            AvatarCache[key] = (url, DateTime.UtcNow);
            return Json(new { url });
        }
        catch
        {
            AvatarCache[key] = (null, DateTime.UtcNow);
            return Json(new { url = (string?)null });
        }
    }

    /// <summary>Bridge bağlantı durumu — UI üst banner'ı için (ready/awaiting_qr/connecting/unreachable).</summary>
    [HttpGet("/Whatsapp/BridgeStatus")]
    public async Task<IActionResult> BridgeStatus(
        [FromServices] IWhatsAppConfigRepository configRepo,
        [FromServices] IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var cfg = await configRepo.GetAsync(ct);
        if (cfg is null || !cfg.IsEnabled)
            return Json(new { ok = false, state = "disabled" });
        if (cfg.Provider != Domain.Entities.WhatsAppProviderType.WebQr)
            return Json(new { ok = true, state = "cloud" }); // Cloud API — bridge yok, webhook push
        if (string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            return Json(new { ok = false, state = "not_configured" });

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var json = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
                cfg.WebQrBridgeUrl.TrimEnd('/') + "/status", ct);
            var state = json.TryGetProperty("state", out var s) ? s.GetString() ?? "unknown" : "unknown";
            return Json(new { ok = state == "ready", state });
        }
        catch
        {
            return Json(new { ok = false, state = "unreachable" });
        }
    }

    /// <summary>JID (@g.us, @s.whatsapp.net) içeriyorsa olduğu gibi bırak; normal telefon ise normalize et.</summary>
    private static string SafePhone(string input)
        => input.Contains('@') ? input : (WaPhoneNormalizer.Normalize(input) ?? string.Empty);

    private static string NormalizePhone(string input)
        => WaPhoneNormalizer.Normalize(input) ?? string.Empty;

    /// <summary>Bir mesajı başka bir sohbete iletir. Mesaj body'sini alıp yeni hedefe gönderir.</summary>
    [HttpPost("/Whatsapp/ForwardMessage")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForwardMessage(
        [FromServices] IWhatsAppService whatsAppService,
        [FromServices] IWaInboxRepository inbox,
        [FromBody] ForwardMessageBody body,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.ToPhone) || string.IsNullOrWhiteSpace(body.MessageId))
            return Json(new { success = false, message = "ToPhone ve MessageId zorunlu." });

        var msg = await inbox.GetByBridgeMsgIdAsync(body.MessageId, ct);
        if (msg is null)
            return Json(new { success = false, message = "Mesaj bulunamadı." });

        var text = msg.Body;
        if (string.IsNullOrWhiteSpace(text))
            return Json(new { success = false, message = "Medya iletme henüz desteklenmiyor." });

        var result = await whatsAppService.SendTextMessageAsync(
            SafePhone(body.ToPhone), $"↗ _İletildi:_\n{text}", ct, interactive: true);
        return Json(new { success = result.Success, message = result.Message });
    }

    /// <summary>Sohbetin tüm mesajlarını siler ama sohbeti sohbet listesinde tutmaz (DeleteConversation'dan farkı bu).</summary>
    [HttpPost("/Whatsapp/ClearChat")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearChat(
        [FromServices] IWaInboxRepository inbox,
        [FromQuery] string phone,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest(new { error = "phone zorunlu" });
        var deleted = await inbox.DeleteConversationAsync(SafePhone(phone), ct);
        return Json(new { success = true, deleted });
    }

    /// <summary>Sohbetin okundu işaretini kaldırır (okunmamış olarak işaretle).</summary>
    [HttpPost("/Whatsapp/MarkUnread")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkUnread(
        [FromServices] IWaInboxRepository inbox,
        [FromQuery] string phone,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest(new { error = "phone zorunlu" });
        var updated = await inbox.MarkUnreadAsync(SafePhone(phone), ct);
        return Json(new { success = true, updated });
    }

    public sealed class ForwardMessageBody
    {
        public string? ToPhone { get; set; }
        public string? MessageId { get; set; }
    }

    /// <summary>
    /// WhatsApp numarası olan kişileri arar — mevcut sohbet olmayan kişiler için.
    /// İki kaynak: 1) CRM Contact.WaPhone  2) Bridge /contacts (telefonun WA rehberi).
    /// </summary>
    [HttpGet("/Whatsapp/ContactSearch")]
    public async Task<IActionResult> ContactSearch(
        [FromServices] SqlServerConnectionFactory connFactory,
        [FromServices] CalibraDatabaseOptions dbOptions,
        [FromServices] IWhatsAppConfigRepository waConfigRepo,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromQuery] string q,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 1)
            return Json(Array.Empty<object>());

        var qTrim = q.Trim();
        var seenPhones = new HashSet<string>(StringComparer.Ordinal);
        var results    = new List<object>();

        // ── 1) CRM Contact tablosu ───────────────────────────────────────
        var s    = string.IsNullOrWhiteSpace(dbOptions.Schema) ? "dbo" : dbOptions.Schema.Trim();
        var like = "%" + qTrim.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";

        await using var conn = await connFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP 20
                [Id],
                COALESCE([WaName], [AccountTitle]) AS DisplayName,
                [AccountTitle],
                [AccountCode],
                [WaPhone]
            FROM [{s}].[Contact]
            WHERE [IsActive] = 1
              AND [WaPhone] IS NOT NULL
              AND [WaPhone] <> ''
              AND (
                [AccountTitle] LIKE @q
                OR [WaName]    LIKE @q
                OR [WaPhone]   LIKE @q
                OR [AccountCode] LIKE @q
              )
            ORDER BY [AccountTitle];
            """;
        cmd.Parameters.Add(new SqlParameter("@q", like));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var rawPhone  = r.GetString(4);
            var normPhone = new string(rawPhone.Where(char.IsDigit).ToArray()) is { Length: > 0 } d ? d : rawPhone;
            if (seenPhones.Add(normPhone))
                results.Add(new
                {
                    id           = r.GetInt32(0),
                    displayName  = r.GetString(1),
                    accountTitle = r.GetString(2),
                    accountCode  = r.IsDBNull(3) ? null : r.GetString(3),
                    phone        = normPhone,
                });
        }
        await r.CloseAsync();

        // ── 2) Bridge /contacts — telefonun WhatsApp rehberi ────────────
        try
        {
            var cfg = await waConfigRepo.GetAsync(ct);
            if (cfg is { IsEnabled: true, Provider: Domain.Entities.WhatsAppProviderType.WebQr }
                && !string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            {
                using var http = httpClientFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(3);
                var url  = cfg.WebQrBridgeUrl.TrimEnd('/') + "/contacts?q=" + Uri.EscapeDataString(qTrim);
                var resp = await http.GetAsync(url, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
                    if (json.TryGetProperty("contacts", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var c in arr.EnumerateArray())
                        {
                            var ph   = c.TryGetProperty("phone", out var pv) ? pv.GetString() ?? "" : "";
                            var name = c.TryGetProperty("name",  out var nv) ? nv.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(ph)) continue;
                            var normPh = new string(ph.Where(char.IsDigit).ToArray()) is { Length: > 0 } d ? d : ph;
                            if (!seenPhones.Add(normPh)) continue; // CRM'de zaten varsa atla
                            results.Add(new
                            {
                                id           = 0,
                                displayName  = name,
                                accountTitle = name,
                                accountCode  = (string?)null,
                                phone        = normPh,
                            });
                        }
                    }
                }
            }
        }
        catch { /* bridge erişilemez ise CRM sonuçlarıyla devam et */ }

        return Json(results.Take(30));
    }
}
