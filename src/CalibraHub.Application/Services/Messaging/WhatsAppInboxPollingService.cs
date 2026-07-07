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
    // 2026-06-24: PollInterval 15sn → 60sn. Sebep: 15sn polling dakikada 4 GET + her
    // tick'te DB sorgu (LastReceivedAt) + her message için resolver çağrısı CPU'yu yoruyor.
    // 60sn yeterli — yeni mesaj 1 dakika gecikme ile gelir, real-time SignalR ile push var.
    // Önceki: 2026-06-20: PollInterval 3sn → 15sn. Sebep: 3sn polling dakikada 20 GET / saatte 1200
    // GET üretiyordu; 1-5 şirket + tek-makine deployment topolojisi için aşırı. 15sn ile yük 5×
    // azalır (saatte 240 GET). Kullanıcıya etki: yeni mesaj görünme gecikmesi 3sn → en kötü 15sn —
    // chat UI için kabul edilebilir. Webhook push'a çevirmek (Bridge → Web) tam refactor gerektirir
    // (WaContact resolve, media download, dedup tarafının webhook handler'a taşınması + retry +
    // Bridge buffer fallback) — gerçek performans metriği gösterene kadar polling kalıyor.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30); // Bridge URL yoksa daha az sik dene

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
                var ext = WaMediaFiles.MimeToExtension(mime);
                var safeId = WaMediaFiles.SafeId(msgId);
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

        // using: response body buffer + connection state dispose edilmeli
        // (her early return'da resp leak oluyordu → saatte 3600 GET birikimi).
        using var _resp = resp;
        if (!resp.IsSuccessStatusCode) return;

        var payload = await resp.Content.ReadFromJsonAsync<BridgeMessagesResponse>(ct);
        if (payload?.Messages is null || payload.Messages.Count == 0) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<WhatsAppInboundProcessor>();

        var maxTs = _sinceCursor;

        foreach (var m in payload.Messages)
        {
            var ts = await processor.ProcessMessageAsync(m, bridgeBase, ct);
            if (ts.HasValue && ts.Value > maxTs) maxTs = ts.Value;
        }

        if (maxTs > _sinceCursor) _sinceCursor = maxTs;
    }

    private sealed class BridgeMessagesResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("messages")]
        public List<BridgeInboundMessage>? Messages { get; set; }
    }
}
