using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.WhatsApp;
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
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
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
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
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
        catch (Exception ex) { return Json(new { success = false, message = ex.Message }); }
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
            return Json(new { ok = false, error = ex.Message });
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
}
