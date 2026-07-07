using CalibraHub.Application.Abstractions.Messaging;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.WhatsApp;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Messaging;

/// <summary>
/// Bridge'den gelen inbound olaylarin ortak isleme mantigi.
/// Iki giris noktasi ayni pipeline'i kullanir:
///  1) WhatsAppInboxPollingService (GET /messages, 60sn — yedek katman)
///  2) WhatsAppBridgeEventsController (bridge'in anlik POST push'u)
/// Dedup InsertIfNotExistsAsync'in UNIQUE bridge_msg_id constraint'i ile saglanir;
/// ayni mesaj iki kanaldan da gelse tek kayit olusur, SignalR push tek atilir.
/// </summary>
public sealed class WhatsAppInboundProcessor
{
    private readonly IWaInboxRepository _inbox;
    private readonly IWaContactResolver _resolver;
    private readonly IWaGroupRepository _groupRepo;
    private readonly IWhatsAppRealTimeNotifier _notifier;
    private readonly IMessageBus _bus;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WhatsAppInboundProcessor> _logger;

    public WhatsAppInboundProcessor(
        IWaInboxRepository inbox,
        IWaContactResolver resolver,
        IWaGroupRepository groupRepo,
        IWhatsAppRealTimeNotifier notifier,
        IMessageBus bus,
        IHttpClientFactory httpClientFactory,
        ILogger<WhatsAppInboundProcessor> logger)
    {
        _inbox             = inbox;
        _resolver          = resolver;
        _groupRepo         = groupRepo;
        _notifier          = notifier;
        _bus               = bus;
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
    }

    /// <summary>
    /// Tek bir bridge mesajini isler: LID resolve, grup kaydi, reaksiyon,
    /// medya indirme, wa_inbox insert, SignalR push, message bus publish.
    /// Islenen mesajin timestamp'ini dondurur (cursor ilerletme icin), atlandiysa null.
    /// </summary>
    public async Task<DateTime?> ProcessMessageAsync(BridgeInboundMessage m, string bridgeBase, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(m.From)) return null;

        // Bridge'in gönderdiği tam JID (varsa); yoksa eski fallback
        var jid   = m.Jid ?? (m.From.Contains('@') ? m.From : m.From + "@s.whatsapp.net");
        var isLid = m.IsLid || WaPhoneNormalizer.IsLid(jid);
        var phone = WaPhoneNormalizer.Normalize(m.From) ?? string.Empty;
        if (string.IsNullOrEmpty(phone)) return null;

        var ts = DateTimeOffset.FromUnixTimeMilliseconds(m.Timestamp).UtcDateTime;

        // WaContact çöz veya oluştur
        int? waContactId = null;
        try
        {
            // LID ise önce Bridge'ten phone resolve dene
            if (isLid)
            {
                var resolvedPhoneJid = await _resolver.ResolveLidToPhoneJidAsync(jid, bridgeBase, ct);
                if (resolvedPhoneJid is not null)
                {
                    var resolvedPhone = WaPhoneNormalizer.Normalize(resolvedPhoneJid);
                    if (resolvedPhone is not null)
                    {
                        phone = resolvedPhone;
                        jid   = resolvedPhoneJid;
                        isLid = false;
                    }
                }
            }

            var waContact = await _resolver.GetOrCreateAsync(jid, m.FromName, ct);
            waContactId = waContact.Id;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[WaInbound] WaContact resolve hatası ({jid}): {msg}", jid, ex.Message);
        }

        // ── Grup mesajı — contact_phone = groupJid, grup kaydı lazım ──────
        var isGroupMessage = !string.IsNullOrWhiteSpace(m.GroupJid);
        if (isGroupMessage)
        {
            var groupSubject = m.SenderName ?? m.GroupJid!;
            try
            {
                await _groupRepo.GetOrCreateAsync(m.GroupJid!, groupSubject, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[WaInbound] Grup kayıt hatası ({jid}): {msg}", m.GroupJid, ex.Message);
            }
            // Grup mesajında sohbet anahtarı grup JID'idir
            phone = m.GroupJid!;
        }

        // ── Reaksiyon mesajı — normal insert değil, hedef mesajı güncelle ──
        if (m.MediaType == "reaction" && !string.IsNullOrWhiteSpace(m.ReactionTargetId))
        {
            var emoji = string.IsNullOrEmpty(m.Body) ? null : m.Body;
            try
            {
                await _inbox.UpdateReactionAsync(m.ReactionTargetId, emoji, ct);
                await _notifier.ReactionUpdatedAsync(m.ReactionTargetId, phone, emoji, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[WaInbound] Reaction update hatası ({id}): {msg}", m.ReactionTargetId, ex.Message);
            }
            return ts;
        }

        // Medyayi indir + diske kaydet (varsa)
        string? mediaPath = null;
        if (m.IsMedia && !string.IsNullOrWhiteSpace(m.MediaUrl) && !string.IsNullOrWhiteSpace(m.Id))
        {
            mediaPath = await TryDownloadAndSaveMediaAsync(bridgeBase, m.Id, m.MediaUrl, m.MediaMime, ts, ct);
        }

        try
        {
            var insertedId = await _inbox.InsertIfNotExistsAsync(new WaInboxMessage
            {
                BridgeMsgId   = m.Id,
                Direction     = m.FromMe ? (byte)1 : (byte)0,
                ContactPhone  = phone,
                ContactId     = waContactId,
                ContactName   = m.FromName,
                Body          = m.Body,
                MediaType     = m.MediaType ?? "chat",
                HasMedia      = m.IsMedia,
                ReceivedAt    = ts,
                CreatedAt     = DateTime.UtcNow,
                MediaPath     = mediaPath,
                MediaMime     = m.MediaMime,
                MediaFileName = m.MediaFileName,
                MediaSize     = m.MediaSize,
                IsLid         = isLid,
                GroupJid      = m.GroupJid,
                SenderJid     = m.SenderJid,
                SenderName    = m.SenderName,
                QuotedMsgId   = m.QuotedMsgId,
            }, ct);

            // Hub'a push sadece gerçekten yeni eklenen mesajlar için —
            // insertedId == null → kayıt zaten vardı (diğer kanal veya Send endpoint'i ekledi).
            if (insertedId.HasValue)
            {
                await _notifier.MessageReceivedAsync(
                    phone, m.Id ?? string.Empty, m.FromMe ? 1 : 0,
                    m.Body, m.MediaType ?? "chat", m.IsMedia,
                    mediaPath, m.MediaMime, m.MediaFileName, m.MediaSize,
                    ts, ct);
                await _notifier.ConversationUpdatedAsync(phone, ct);
                await _bus.PublishAsync(new WhatsAppMessageReceived
                {
                    ContactPhone = phone,
                    Body         = m.Body,
                    IsIncoming   = !m.FromMe,
                    MediaType    = m.MediaType ?? "chat",
                    BridgeMsgId  = m.Id,
                    At           = ts,
                }, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WaInbound] insert basarisiz: {id}", m.Id);
        }

        return ts;
    }

    /// <summary>Mesaj durumu (sent/delivered/read) — DB'ye yaz + UI'a push (tikler).</summary>
    public async Task ProcessReceiptAsync(string messageId, string phone, string status, CancellationToken ct)
    {
        try
        {
            await _inbox.UpdateDeliveryStatusAsync(messageId, status, ct);
            await _notifier.MessageStatusUpdatedAsync(messageId, phone, status, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[WaInbound] receipt islenemedi ({id}/{status}): {msg}", messageId, status, ex.Message);
        }
    }

    /// <summary>
    /// Presence — DB'ye yazilmaz, sadece UI'a push edilir.
    /// status: typing | paused | online | offline
    /// </summary>
    public async Task ProcessPresenceAsync(string phone, string status, DateTime? lastSeen, CancellationToken ct)
    {
        try
        {
            switch (status)
            {
                case "typing":
                    await _notifier.TypingUpdatedAsync(phone, true, ct);
                    break;
                case "paused":
                    await _notifier.TypingUpdatedAsync(phone, false, ct);
                    break;
                case "online":
                    await _notifier.PresenceUpdatedAsync(phone, "online", null, ct);
                    break;
                case "offline":
                    await _notifier.PresenceUpdatedAsync(phone, "offline", lastSeen, ct);
                    // Cevrimdisi olduysa yaziyor gostergesi de kapansin
                    await _notifier.TypingUpdatedAsync(phone, false, ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[WaInbound] presence islenemedi ({phone}/{status}): {msg}", phone, status, ex.Message);
        }
    }

    /// <summary>Karşı taraf mesajı sildi (revoke) — DB'de gizle + UI'a push.</summary>
    public async Task ProcessRevokeAsync(string messageId, string phone, CancellationToken ct)
    {
        try
        {
            await _inbox.MarkDeletedAsync(messageId, ct);
            await _notifier.MessageDeletedAsync(messageId, phone, ct);
            await _notifier.ConversationUpdatedAsync(phone, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[WaInbound] revoke islenemedi ({id}): {msg}", messageId, ex.Message);
        }
    }

    /// <summary>
    /// Bridge'den medya bytelarini indirir, wwwroot/uploads/whatsapp/yyyy/MM/&lt;id&gt;.&lt;ext&gt; kaydeder,
    /// relative URL doner. Dosya zaten varsa indirme atlanir (idempotent).
    /// </summary>
    private async Task<string?> TryDownloadAndSaveMediaAsync(
        string bridgeBase, string msgId, string mediaPathOnBridge, string? mime, DateTime receivedAt, CancellationToken ct)
    {
        try
        {
            var effectiveMime = mime;
            if (!string.IsNullOrEmpty(effectiveMime))
            {
                var preExt     = WaMediaFiles.MimeToExtension(effectiveMime);
                var preSafeId  = WaMediaFiles.SafeId(msgId);
                var preSubPath = Path.Combine("uploads", "whatsapp",
                    receivedAt.Year.ToString(), receivedAt.Month.ToString("D2"));
                var preFullPath = Path.Combine(
                    Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
                    preSubPath, $"{preSafeId}.{preExt}");
                if (File.Exists(preFullPath))
                {
                    return "/" + preSubPath.Replace('\\', '/') + "/" + preSafeId + "." + preExt;
                }
            }

            var url = bridgeBase + mediaPathOnBridge;
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0) return null;

            effectiveMime = mime ?? resp.Content.Headers.ContentType?.MediaType;
            var ext    = WaMediaFiles.MimeToExtension(effectiveMime);
            var safeId = WaMediaFiles.SafeId(msgId);

            var wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var subPath = Path.Combine("uploads", "whatsapp",
                receivedAt.Year.ToString(), receivedAt.Month.ToString("D2"));
            var dir = Path.Combine(wwwroot, subPath);
            Directory.CreateDirectory(dir);
            var fullPath = Path.Combine(dir, $"{safeId}.{ext}");
            if (File.Exists(fullPath))
            {
                return "/" + subPath.Replace('\\', '/') + "/" + safeId + "." + ext;
            }
            await File.WriteAllBytesAsync(fullPath, bytes, ct);

            var urlPath = "/" + subPath.Replace('\\', '/') + "/" + safeId + "." + ext;
            _logger.LogInformation("[WaInbound] medya kaydedildi: {url} ({size} byte)", urlPath, bytes.Length);
            return urlPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WaInbound] medya indirilemedi: {id}", msgId);
            return null;
        }
    }
}

/// <summary>Medya dosya adi/uzanti yardimcilari — processor + polling backfill ortak kullanir.</summary>
public static class WaMediaFiles
{
    public static string SafeId(string id)
    {
        var sb = new System.Text.StringBuilder(id.Length);
        foreach (var c in id)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return sb.ToString();
    }

    public static string MimeToExtension(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime)) return "bin";
        var m = mime.Split(';')[0].Trim().ToLowerInvariant();
        return m switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            "image/gif" => "gif",
            "video/mp4" => "mp4",
            "video/3gpp" => "3gp",
            "video/quicktime" => "mov",
            "audio/ogg" => "ogg",
            "audio/mpeg" => "mp3",
            "audio/mp4" => "m4a",
            "audio/aac" => "aac",
            "audio/wav" => "wav",
            "audio/webm" => "webm",
            "application/pdf" => "pdf",
            "application/msword" => "doc",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "docx",
            "application/vnd.ms-excel" => "xls",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "xlsx",
            "text/plain" => "txt",
            _ => m.Contains('/') ? m.Split('/')[1] : "bin",
        };
    }
}
