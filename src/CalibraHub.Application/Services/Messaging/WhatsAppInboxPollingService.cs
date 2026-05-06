using System.Net.Http.Json;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
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
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(15); // Bridge URL yoksa daha az sik dene

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WhatsAppInboxPollingService> _logger;

    private DateTime _sinceCursor = DateTime.MinValue; // ilk tickte DB'den restore edilir
    private bool _backfillDone = false;                 // session basina bir kez calissin

    public WhatsAppInboxPollingService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<WhatsAppInboxPollingService> logger)
    {
        _scopeFactory       = scopeFactory;
        _httpClientFactory  = httpClientFactory;
        _logger             = logger;
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
        var inbox = scope.ServiceProvider.GetRequiredService<IWaInboxRepository>();

        var now = DateTime.UtcNow;
        var maxTs = _sinceCursor;

        foreach (var m in payload.Messages)
        {
            if (string.IsNullOrWhiteSpace(m.From)) continue;
            var phone = NormalizePhone(m.From);
            if (string.IsNullOrEmpty(phone)) continue;

            var ts = DateTimeOffset.FromUnixTimeMilliseconds(m.Timestamp).UtcDateTime;
            if (ts > maxTs) maxTs = ts;

            // Medyayi indir + diske kaydet (varsa)
            string? mediaPath = null;
            if (m.IsMedia && !string.IsNullOrWhiteSpace(m.MediaUrl) && !string.IsNullOrWhiteSpace(m.Id))
            {
                mediaPath = await TryDownloadAndSaveMediaAsync(bridgeBase, m.Id, m.MediaUrl, m.MediaMime, ts, ct);
            }

            try
            {
                await inbox.InsertIfNotExistsAsync(new WaInboxMessage
                {
                    BridgeMsgId   = m.Id,
                    Direction     = m.FromMe ? (byte)1 : (byte)0,
                    ContactPhone  = phone,
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
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WaPolling] insert basarisiz: {id}", m.Id);
            }
        }

        if (maxTs > _sinceCursor) _sinceCursor = maxTs;
    }

    private static string NormalizePhone(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input.Trim())
            if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
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
    }
}
