using System.Net.Http.Json;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Messaging;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.WhatsApp;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Messaging;

/// <summary>
/// Bridge'in /messages ucunu periyodik poll'lar; yeni gelen/giden mesajlari
/// wa_inbox'a yazar (UNIQUE bridge_msg_id ile dedup).
/// CalibraHub.Web ayaga kalktiginda baslar, durdugunda durur.
/// </summary>
public sealed class WhatsAppInboxPollingService : BackgroundService
{
    // 2026-06-20: PollInterval 3sn → 15sn. Sebep: 3sn polling dakikada 20 GET / saatte 1200
    // GET üretiyordu; 1-5 şirket + tek-makine deployment topolojisi için aşırı. 15sn ile yük 5×
    // azalır (saatte 240 GET). Kullanıcıya etki: yeni mesaj görünme gecikmesi 3sn → en kötü 15sn —
    // chat UI için kabul edilebilir. Webhook push'a çevirmek (Bridge → Web) tam refactor gerektirir
    // (WaContact resolve, media download, dedup tarafının webhook handler'a taşınması + retry +
    // Bridge buffer fallback) — gerçek performans metriği gösterene kadar polling kalıyor.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30); // Bridge URL yoksa daha az sik dene

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WhatsAppInboxPollingService> _logger;
    private readonly IWhatsAppRealTimeNotifier _notifier;
    private readonly IMessageBus _bus;

    private DateTime _sinceCursor = DateTime.MinValue; // ilk tickte DB'den restore edilir
    private bool _backfillDone = false;                 // session basina bir kez calissin

    public WhatsAppInboxPollingService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<WhatsAppInboxPollingService> logger,
        IWhatsAppRealTimeNotifier notifier,
        IMessageBus bus)
    {
        _scopeFactory       = scopeFactory;
        _httpClientFactory  = httpClientFactory;
        _logger             = logger;
        _notifier           = notifier;
        _bus                = bus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[WaPolling] Bridge poll servisi basladi (her {sec}sn).", PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var bridgeUrl = await TryGetBridgeUrlAsync(stoppingToken);
            if (string.IsNullOrWhiteSpace(bridgeUrl))
            {
                await Task.Delay(IdleInterval, stoppingToken);
                continue;
            }

            try
            {
                if (_sinceCursor == DateTime.MinValue)
                {
                    _sinceCursor = await GetLastReceivedAtAsync(stoppingToken) ?? DateTime.UtcNow.AddMinutes(-5);
                }

                // Tek seferlik backfill — DB'de medya icin path olmayan eski kayitlari Bridge'den cek
                if (!_backfillDone)
                {
                    await BackfillMissingMediaAsync(bridgeUrl, stoppingToken);
                    _backfillDone = true;
                }

                await PollOnceAsync(bridgeUrl, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WaPolling] Tick hatasi (devam edilecek).");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("[WaPolling] Servis durdu.");
    }

    private async Task<string?> TryGetBridgeUrlAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var configRepo = scope.ServiceProvider.GetRequiredService<IWhatsAppConfigRepository>();
            var cfg = await configRepo.GetAsync(ct);
            if (cfg is null || !cfg.IsEnabled) return null;
            if (cfg.Provider != WhatsAppProviderType.WebQr) return null;
            return string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl) ? null : cfg.WebQrBridgeUrl.TrimEnd('/');
        }
        catch
        {
            return null; // erken acilis veya DB yok
        }
    }

    private async Task<DateTime?> GetLastReceivedAtAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IWaInboxRepository>();
        return await inbox.GetLastReceivedAtAsync(ct);
    }

    private async Task BackfillMissingMediaAsync(string bridgeBase, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IWaInboxRepository>();
        var missing = await inbox.GetMediaMessagesMissingFileAsync(50, ct);
        if (missing.Count == 0)
        {
            _logger.LogInformation("[WaPolling] Backfill: eksik medya yok.");
            return;
        }
        _logger.LogInformation("[WaPolling] Backfill: {n} medya mesaji islenecek.", missing.Count);

        foreach (var (id, msgId) in missing)
        {
            try
            {
                using var http = _httpClientFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(15);
                using var resp = await http.GetAsync($"{bridgeBase}/media/{Uri.EscapeDataString(msgId)}", HttpCompletionOption.ResponseHeadersRead, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogDebug("[WaPolling] Backfill: bridge'de medya yok ({id}, HTTP {status})", msgId, (int)resp.StatusCode);
                    continue;
                }
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length == 0) continue;

                var mime = resp.Content.Headers.ContentType?.MediaType;
                var ext = MimeToExtension(mime);
                var safeId = SafeId(msgId);
                var receivedAt = DateTime.UtcNow; // exact tarih unutuldu, simdiki ay klasoru
                var wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var subPath = Path.Combine("uploads", "whatsapp", receivedAt.Year.ToString(), receivedAt.Month.ToString("D2"));
                var dir = Path.Combine(wwwroot, subPath);
                Directory.CreateDirectory(dir);
                var fullPath = Path.Combine(dir, $"{safeId}.{ext}");
                await File.WriteAllBytesAsync(fullPath, bytes, ct);
                var urlPath = "/" + subPath.Replace('\\', '/') + "/" + safeId + "." + ext;
                await inbox.UpdateMediaPathAsync(id, urlPath, mime, null, bytes.Length, ct);
                _logger.LogInformation("[WaPolling] Backfill OK: {url} ({size} byte)", urlPath, bytes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WaPolling] Backfill hata: {id}", msgId);
            }
        }
    }

    private async Task PollOnceAsync(string bridgeBase, CancellationToken ct)
    {
        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(5);

        var sinceIso = _sinceCursor.ToString("o"); // ISO-8601
        var url = $"{bridgeBase}/messages?since={Uri.EscapeDataString(sinceIso)}";

        HttpResponseMessage resp;
        try { resp = await http.GetAsync(url, ct); }
        catch (HttpRequestException) { return; }   // Bridge kapali — bir sonraki tick
        catch (TaskCanceledException) { return; }  // timeout

        if (!resp.IsSuccessStatusCode) return;

        var payload = await resp.Content.ReadFromJsonAsync<BridgeMessagesResponse>(ct);
        if (payload?.Messages is null || payload.Messages.Count == 0) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var inbox      = scope.ServiceProvider.GetRequiredService<IWaInboxRepository>();
        var resolver   = scope.ServiceProvider.GetRequiredService<IWaContactResolver>();
        var groupRepo  = scope.ServiceProvider.GetRequiredService<IWaGroupRepository>();

        var now   = DateTime.UtcNow;
        var maxTs = _sinceCursor;

        foreach (var m in payload.Messages)
        {
            if (string.IsNullOrWhiteSpace(m.From)) continue;

            // Bridge'in gönderdiği tam JID (varsa); yoksa eski fallback
            var jid   = m.Jid ?? (m.From.Contains('@') ? m.From : m.From + "@s.whatsapp.net");
            var isLid = m.IsLid || WaPhoneNormalizer.IsLid(jid);
            var phone = WaPhoneNormalizer.Normalize(m.From) ?? string.Empty;
            if (string.IsNullOrEmpty(phone)) continue;

            var ts = DateTimeOffset.FromUnixTimeMilliseconds(m.Timestamp).UtcDateTime;
            if (ts > maxTs) maxTs = ts;

            // WaContact çöz veya oluştur
            int? waContactId = null;
            try
            {
                // LID ise önce Bridge'ten phone resolve dene
                if (isLid)
                {
                    var resolvedPhoneJid = await resolver.ResolveLidToPhoneJidAsync(jid, bridgeBase, ct);
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

                var waContact = await resolver.GetOrCreateAsync(jid, m.FromName, ct);
                waContactId = waContact.Id;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[WaPolling] WaContact resolve hatası ({jid}): {msg}", jid, ex.Message);
            }

            // ── Grup mesajı — contact_phone = groupJid, grup kaydı lazım ──────
            var isGroupMessage = !string.IsNullOrWhiteSpace(m.GroupJid);
            if (isGroupMessage)
            {
                var groupSubject = m.SenderName ?? m.GroupJid!;
                try
                {
                    await groupRepo.GetOrCreateAsync(m.GroupJid!, groupSubject, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("[WaPolling] Grup kayıt hatası ({jid}): {msg}", m.GroupJid, ex.Message);
                }
                // Grup mesajında contact_phone = groupJid; phone değişkeni düzeltme
                phone = m.GroupJid!;
            }

            // ── Reaksiyon mesajı — normal insert değil, hedef mesajı güncelle ──
            if (m.MediaType == "reaction" && !string.IsNullOrWhiteSpace(m.ReactionTargetId))
            {
                var emoji = string.IsNullOrEmpty(m.Body) ? null : m.Body;
                try
                {
                    await inbox.UpdateReactionAsync(m.ReactionTargetId, emoji, ct);
                    await _notifier.ReactionUpdatedAsync(m.ReactionTargetId, phone, emoji, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("[WaPolling] Reaction update hatası ({id}): {msg}", m.ReactionTargetId, ex.Message);
                }
                if (ts > maxTs) maxTs = ts;
                continue;
            }

            // Medyayi indir + diske kaydet (varsa)
            string? mediaPath = null;
            if (m.IsMedia && !string.IsNullOrWhiteSpace(m.MediaUrl) && !string.IsNullOrWhiteSpace(m.Id))
            {
                mediaPath = await TryDownloadAndSaveMediaAsync(bridgeBase, m.Id, m.MediaUrl, m.MediaMime, ts, ct);
            }

            try
            {
                var insertedId = await inbox.InsertIfNotExistsAsync(new WaInboxMessage
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
                    CreatedAt     = now,
                    MediaPath     = mediaPath,
                    MediaMime     = m.MediaMime,
                    MediaFileName = m.MediaFileName,
                    MediaSize     = m.MediaSize,
                    IsLid         = isLid,
                    GroupJid      = m.GroupJid,
                    SenderJid     = m.SenderJid,
                    SenderName    = m.SenderName,
                }, ct);

                // Hub'a push sadece gerçekten yeni eklenen mesajlar için yapılır.
                // insertedId == null → kayıt zaten vardı (SendMedia/Send tarafından eklendi);
                // tekrar push etmek frontend'de çift mesaj balonuna yol açar.
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
                _logger.LogWarning(ex, "[WaPolling] insert basarisiz: {id}", m.Id);
            }
        }

        if (maxTs > _sinceCursor) _sinceCursor = maxTs;
    }

    /// <summary>
    /// Bridge'den medya bytelarini indirir, wwwroot/uploads/whatsapp/yyyy/MM/<id>.<ext> kaydeder,
    /// '/uploads/whatsapp/yyyy/MM/<id>.<ext>' relative URL'ini doner (UI direkt erisir).
    /// </summary>
    private async Task<string?> TryDownloadAndSaveMediaAsync(
        string bridgeBase, string msgId, string mediaPathOnBridge, string? mime, DateTime receivedAt, CancellationToken ct)
    {
        try
        {
            // bridgeBase = http://127.0.0.1:61100, mediaPathOnBridge = /media/<id>
            var url = bridgeBase + mediaPathOnBridge;
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30); // Buyuk dosyalar icin 30sn
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0) return null;

            // Content-Type header'dan mime'i da yakala (yedek)
            var effectiveMime = mime ?? resp.Content.Headers.ContentType?.MediaType;
            var ext = MimeToExtension(effectiveMime);
            var safeId = SafeId(msgId);

            // wwwroot/uploads/whatsapp/yyyy/MM/<id>.<ext>
            var wwwroot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var subPath = Path.Combine("uploads", "whatsapp",
                receivedAt.Year.ToString(), receivedAt.Month.ToString("D2"));
            var dir = Path.Combine(wwwroot, subPath);
            Directory.CreateDirectory(dir);
            var fullPath = Path.Combine(dir, $"{safeId}.{ext}");
            await File.WriteAllBytesAsync(fullPath, bytes, ct);

            // UI'a verilecek relative URL — Forward slash, leading /
            var urlPath = "/" + subPath.Replace('\\', '/') + "/" + safeId + "." + ext;
            _logger.LogInformation("[WaPolling] medya kaydedildi: {url} ({size} byte)", urlPath, bytes.Length);
            return urlPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WaPolling] medya indirilemedi: {id}", msgId);
            return null;
        }
    }

    private static string SafeId(string id)
    {
        var sb = new System.Text.StringBuilder(id.Length);
        foreach (var c in id)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return sb.ToString();
    }

    private static string MimeToExtension(string? mime)
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
            "application/pdf" => "pdf",
            "application/msword" => "doc",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "docx",
            "application/vnd.ms-excel" => "xls",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "xlsx",
            "text/plain" => "txt",
            _ => m.Contains('/') ? m.Split('/')[1] : "bin",
        };
    }

    private sealed class BridgeMessagesResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("messages")]
        public List<BridgeMessage>? Messages { get; set; }
    }

    private sealed class BridgeMessage
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("from")]
        public string? From { get; set; }

        /// <summary>Tam JID: 905...@s.whatsapp.net veya 178...@lid. Bridge'in yeni versiyonunda gönderilir.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("jid")]
        public string? Jid { get; set; }

        /// <summary>LID identifier mı (gerçek telefon numarası değil)?</summary>
        [System.Text.Json.Serialization.JsonPropertyName("isLid")]
        public bool IsLid { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("fromName")]
        public string? FromName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("fromMe")]
        public bool FromMe { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("body")]
        public string? Body { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("isMedia")]
        public bool IsMedia { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("mediaType")]
        public string? MediaType { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("mediaUrl")]
        public string? MediaUrl { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("mediaMime")]
        public string? MediaMime { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("mediaFileName")]
        public string? MediaFileName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("mediaSize")]
        public int? MediaSize { get; set; }

        /// <summary>Reaksiyon mesajı: hedef mesajın bridge ID'si.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("reactionTargetId")]
        public string? ReactionTargetId { get; set; }

        /// <summary>Grup mesajı: grubun JID'i (@g.us). Null ise 1:1 sohbet.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("groupJid")]
        public string? GroupJid { get; set; }

        /// <summary>Grup mesajı: mesajı gönderen üyenin JID'i.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("senderJid")]
        public string? SenderJid { get; set; }

        /// <summary>Grup mesajı: mesajı gönderen üyenin adı (pushName).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("senderName")]
        public string? SenderName { get; set; }
    }
}
