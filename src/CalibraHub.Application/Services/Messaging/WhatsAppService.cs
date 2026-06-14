using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Security;
using CalibraHub.Application.WhatsApp;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Messaging;

/// <summary>
/// Meta WhatsApp Cloud API client.
/// Endpoint: https://graph.facebook.com/v21.0/{phone-number-id}/messages
/// Auth:     Bearer token (DPAPI sifreli, DB'den decrypt)
/// </summary>
public sealed class WhatsAppService : IWhatsAppService
{
    private const string GraphApiBase = "https://graph.facebook.com/v21.0";

    private readonly IWhatsAppConfigRepository _repo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WhatsAppService> _logger;
    private readonly WhatsAppSafetyChecker _safety;
    private readonly IWhatsAppSendLogRepository _logRepo;

    public WhatsAppService(
        IWhatsAppConfigRepository repo,
        IHttpClientFactory httpClientFactory,
        ILogger<WhatsAppService> logger,
        WhatsAppSafetyChecker safety,
        IWhatsAppSendLogRepository logRepo)
    {
        _repo               = repo;
        _httpClientFactory  = httpClientFactory;
        _logger             = logger;
        _safety             = safety;
        _logRepo            = logRepo;
    }

    public Task<WhatsAppConfig?> GetConfigAsync(CancellationToken cancellationToken)
        => _repo.GetAsync(cancellationToken);

    public async Task<WhatsAppConfigSaveResult> SaveConfigAsync(
        string accessToken,
        string phoneNumberId,
        string? businessAccountId,
        string? webhookVerifyToken,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumberId)) return new(false, "Phone Number ID bos olamaz.");

        // Mevcut config'i koru, sadece set edilen alanlari guncelle
        var existing = await _repo.GetAsync(cancellationToken) ?? new WhatsAppConfig
        {
            Id = 1,
            CreatedAt = DateTime.UtcNow,
        };

        // Token boş ise mevcudunu koru; doluysa DPAPI ile şifrele
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            try
            {
                existing.AccessTokenEncrypted = DpapiSecretDecryptor.Encrypt(accessToken.Trim());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DPAPI sifreleme basarisiz, plaintext sakliyoruz (gelistirme/non-Windows).");
                existing.AccessTokenEncrypted = accessToken.Trim();
            }
        }
        else if (string.IsNullOrEmpty(existing.AccessTokenEncrypted))
        {
            // Yeni kayıtta token boş gelirse hata
            return new(false, "Access token bos olamaz (ilk kayıt).");
        }

        existing.PhoneNumberId      = phoneNumberId.Trim();
        existing.BusinessAccountId  = businessAccountId?.Trim();
        existing.WebhookVerifyToken = webhookVerifyToken?.Trim();
        existing.IsEnabled          = isEnabled;
        existing.UpdatedAt          = DateTime.UtcNow;
        if (existing.CreatedAt == default) existing.CreatedAt = DateTime.UtcNow;

        await _repo.SaveAsync(existing, cancellationToken);
        return new(true, "Yapilandirma kaydedildi.");
    }

    public async Task<WhatsAppTestResult> TestConfigAsync(CancellationToken cancellationToken)
    {
        var cfg = await _repo.GetAsync(cancellationToken);
        if (cfg is null) return new(false, "Yapilandirma yok.", null);

        // Web QR provider: Bridge'e health check yap
        if (cfg.Provider == Domain.Entities.WhatsAppProviderType.WebQr)
        {
            return await TestWebQrBridgeAsync(cfg, cancellationToken);
        }

        // Cloud API: Meta'ya numara bilgisi sorgusu
        if (string.IsNullOrEmpty(cfg.AccessTokenEncrypted) || string.IsNullOrEmpty(cfg.PhoneNumberId))
            return new(false, "Yapilandirma eksik. Once Access Token ve Phone Number ID girip kaydet.", null);

        var token = DpapiSecretDecryptor.DecryptIfNeeded(cfg.AccessTokenEncrypted);

        // Meta Graph API: GET /{phone-number-id} → numara bilgisi
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.Timeout = TimeSpan.FromSeconds(10);

            var resp = await client.GetAsync($"{GraphApiBase}/{cfg.PhoneNumberId}?fields=display_phone_number,verified_name", cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                cfg.LastError  = $"Test basarisiz: HTTP {(int)resp.StatusCode} — {Truncate(body, 200)}";
                cfg.UpdatedAt  = DateTime.UtcNow;
                await _repo.SaveAsync(cfg, cancellationToken);
                return new(false, cfg.LastError, null);
            }

            using var doc = JsonDocument.Parse(body);
            var displayNumber = doc.RootElement.TryGetProperty("display_phone_number", out var dp) ? dp.GetString() : null;
            var verifiedName  = doc.RootElement.TryGetProperty("verified_name",        out var vn) ? vn.GetString() : null;

            cfg.DisplayPhoneNumber = displayNumber;
            cfg.LastError          = null;
            cfg.UpdatedAt          = DateTime.UtcNow;
            await _repo.SaveAsync(cfg, cancellationToken);

            return new(true, $"Baglanti basarili. Numara: {displayNumber ?? "?"} — {verifiedName ?? ""}", displayNumber);
        }
        catch (Exception ex)
        {
            cfg.LastError = $"Test exception: {ex.Message}";
            cfg.UpdatedAt = DateTime.UtcNow;
            await _repo.SaveAsync(cfg, cancellationToken);
            return new(false, cfg.LastError, null);
        }
    }

    public async Task<WhatsAppSendResult> SendTextMessageAsync(string toPhone, string message, CancellationToken cancellationToken, bool interactive = false)
    {
        if (string.IsNullOrWhiteSpace(toPhone))  return new(false, "Hedef numara bos olamaz.", null);
        if (string.IsNullOrWhiteSpace(message))  return new(false, "Mesaj bos olamaz.", null);

        var cfg = await _repo.GetAsync(cancellationToken);
        if (cfg is null || !cfg.IsEnabled)
            return new(false, "WhatsApp pasif. Sirket Ayarlari → WhatsApp sekmesinden etkinlestir.", null);

        // Provider'a gore zorunlu alan kontrolu — Web QR icin Token/PhoneNumberId beklemiyoruz
        if (cfg.Provider == Domain.Entities.WhatsAppProviderType.WebQr)
        {
            if (string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
                return new(false, "Web QR Bridge URL bos. Sirket Ayarlari → WhatsApp sekmesinden gir.", null);
        }
        else // CloudApi
        {
            if (string.IsNullOrEmpty(cfg.AccessTokenEncrypted) || string.IsNullOrEmpty(cfg.PhoneNumberId))
                return new(false, "Cloud API yapilandirmasi eksik (Access Token / Phone Number ID).", null);
        }

        // ── SAFETY CHECK ─────────────────────────────────────────────────────
        // Interactive chat: rate limit atlanir (kullanici elle yaziyor, spam yok).
        var safetyResult = await _safety.CheckAsync(toPhone, message, cancellationToken, interactive);
        if (!safetyResult.Allowed)
        {
            // Reddedilen girişimi de log'a yaz — istatistik için
            await _logRepo.InsertAsync(new Domain.Entities.WhatsAppSendLog
            {
                SentAt      = DateTime.UtcNow,
                ToPhone     = NormalizePhone(toPhone),
                MessageHash = null,
                Success     = false,
                BlockReason = safetyResult.RejectReason,
            }, cancellationToken);

            var waitMsg = safetyResult.SuggestedWaitMinutes > 0
                ? $" ({safetyResult.SuggestedWaitMinutes} dk sonra tekrar dene)" : "";
            return new(false, $"Safety reddi: {safetyResult.RejectReason}{waitMsg}", null);
        }

        // İnsan-benzeri rastgele gecikme (3-15 sn) — sadece otomasyon/toplu gonderim icin.
        // Interactive UI'dan (chat ekrani) geliyorsa atlanir; cunku kullanici zaten elle yaziyor.
        if (!interactive && safetyResult.Rules is not null)
        {
            var delay = _safety.ComputeHumanDelay(safetyResult.Rules);
            await Task.Delay(delay, cancellationToken);
        }

        var to = NormalizePhone(toPhone);

        // Web QR provider: Bridge'e POST /send
        if (cfg.Provider == Domain.Entities.WhatsAppProviderType.WebQr)
        {
            return await SendViaBridgeAsync(cfg, to, message, safetyResult.MessageHash, cancellationToken);
        }

        // Cloud API path
        if (string.IsNullOrEmpty(cfg.AccessTokenEncrypted) || string.IsNullOrEmpty(cfg.PhoneNumberId))
            return new(false, "Cloud API config eksik (Token / Phone Number ID).", null);

        var token = DpapiSecretDecryptor.DecryptIfNeeded(cfg.AccessTokenEncrypted);

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.Timeout = TimeSpan.FromSeconds(15);

            var payload = new
            {
                messaging_product = "whatsapp",
                recipient_type    = "individual",
                to                = to,
                type              = "text",
                text              = new { body = message },
            };

            var resp = await client.PostAsJsonAsync(
                $"{GraphApiBase}/{cfg.PhoneNumberId}/messages",
                payload, cancellationToken);

            var body = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                cfg.LastError = $"Send fail HTTP {(int)resp.StatusCode}: {Truncate(body, 300)}";
                cfg.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveAsync(cfg, cancellationToken);

                // Audit log
                await _logRepo.InsertAsync(new Domain.Entities.WhatsAppSendLog
                {
                    SentAt       = DateTime.UtcNow,
                    ToPhone      = to,
                    MessageHash  = safetyResult.MessageHash,
                    Success      = false,
                    ErrorMessage = cfg.LastError,
                }, cancellationToken);

                return new(false, cfg.LastError, null);
            }

            using var doc = JsonDocument.Parse(body);
            var messageId = doc.RootElement.TryGetProperty("messages", out var msgs) && msgs.GetArrayLength() > 0
                ? msgs[0].TryGetProperty("id", out var idEl) ? idEl.GetString() : null
                : null;

            cfg.LastSuccessfulSendAt = DateTime.UtcNow;
            cfg.LastError            = null;
            cfg.UpdatedAt            = DateTime.UtcNow;
            await _repo.SaveAsync(cfg, cancellationToken);

            // Audit log — başarı
            await _logRepo.InsertAsync(new Domain.Entities.WhatsAppSendLog
            {
                SentAt      = DateTime.UtcNow,
                ToPhone     = to,
                MessageHash = safetyResult.MessageHash,
                MessageId   = messageId,
                Success     = true,
            }, cancellationToken);

            return new(true, $"Mesaj gonderildi. ID: {messageId}", messageId);
        }
        catch (Exception ex)
        {
            cfg.LastError = $"Send exception: {ex.Message}";
            cfg.UpdatedAt = DateTime.UtcNow;
            await _repo.SaveAsync(cfg, cancellationToken);

            await _logRepo.InsertAsync(new Domain.Entities.WhatsAppSendLog
            {
                SentAt       = DateTime.UtcNow,
                ToPhone      = to,
                MessageHash  = safetyResult.MessageHash,
                Success      = false,
                ErrorMessage = ex.Message,
            }, cancellationToken);

            return new(false, cfg.LastError, null);
        }
    }

    /// <summary>Web QR Node Bridge'i üzerinden mesaj gönder. Stabilite: short timeout + audit log.</summary>
    private async Task<WhatsAppSendResult> SendViaBridgeAsync(
        Domain.Entities.WhatsAppConfig cfg, string to, string message, string? messageHash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            return new(false, "Bridge URL bos. Sirket Ayarlari > WhatsApp'tan ayarla.", null);

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var sendUrl = cfg.WebQrBridgeUrl.TrimEnd('/') + "/send";
            var resp = await client.PostAsJsonAsync(sendUrl, new { to, text = message }, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                cfg.LastError = $"Bridge HTTP {(int)resp.StatusCode}: {Truncate(body, 250)}";
                cfg.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveAsync(cfg, ct);

                await _logRepo.InsertAsync(new Domain.Entities.WhatsAppSendLog
                {
                    SentAt = DateTime.UtcNow, ToPhone = to, MessageHash = messageHash,
                    Success = false, ErrorMessage = cfg.LastError,
                }, ct);
                return new(false, cfg.LastError, null);
            }

            using var doc = JsonDocument.Parse(body);
            var ok = doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
            var msgId = doc.RootElement.TryGetProperty("messageId", out var midEl) ? midEl.GetString() : null;

            if (!ok)
            {
                var errMsg = doc.RootElement.TryGetProperty("error", out var errEl) ? errEl.GetString() : "Bilinmeyen Bridge hatasi";
                cfg.LastError = errMsg;
                cfg.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveAsync(cfg, ct);
                await _logRepo.InsertAsync(new Domain.Entities.WhatsAppSendLog
                {
                    SentAt = DateTime.UtcNow, ToPhone = to, MessageHash = messageHash,
                    Success = false, ErrorMessage = errMsg,
                }, ct);
                return new(false, errMsg, null);
            }

            cfg.LastSuccessfulSendAt = DateTime.UtcNow;
            cfg.LastError = null;
            cfg.UpdatedAt = DateTime.UtcNow;
            await _repo.SaveAsync(cfg, ct);

            await _logRepo.InsertAsync(new Domain.Entities.WhatsAppSendLog
            {
                SentAt = DateTime.UtcNow, ToPhone = to, MessageHash = messageHash,
                MessageId = msgId, Success = true,
            }, ct);
            return new(true, $"Bridge ile gonderildi. ID: {msgId}", msgId);
        }
        catch (TaskCanceledException)
        {
            var err = "Bridge zaman asimi (15sn). Bridge calisiyor mu? Hedef numara WhatsApp'ta var mi?";
            await _logRepo.InsertAsync(new Domain.Entities.WhatsAppSendLog
            {
                SentAt = DateTime.UtcNow, ToPhone = to, MessageHash = messageHash,
                Success = false, ErrorMessage = err,
            }, ct);
            return new(false, err, null);
        }
        catch (HttpRequestException ex)
        {
            var err = $"Bridge'e baglanilamadi: {ex.Message}. Bridge servisi calisir durumda mi?";
            await _logRepo.InsertAsync(new Domain.Entities.WhatsAppSendLog
            {
                SentAt = DateTime.UtcNow, ToPhone = to, MessageHash = messageHash,
                Success = false, ErrorMessage = err,
            }, ct);
            return new(false, err, null);
        }
    }

    /// <summary>
    /// Bridge'in /status + (gerekirse) /qr ucunu siralarinca okur ve frontend'e tek payload doner.
    /// Mixed-content/CORS'u atlatmak icin tarayici degil server cagiriyor.
    /// </summary>
    public async Task<WhatsAppQrStatusResult> GetWebQrStatusAsync(CancellationToken cancellationToken)
    {
        var cfg = await _repo.GetAsync(cancellationToken);
        if (cfg is null || cfg.Provider != Domain.Entities.WhatsAppProviderType.WebQr)
            return new(false, "disabled", null, null, null, "Web QR provider seçili degil.");
        if (string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            return new(false, "no_url", null, null, null, "Bridge URL bos.");

        var baseUrl = cfg.WebQrBridgeUrl.TrimEnd('/');

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(4);

            // /status
            var statusResp = await client.GetAsync(baseUrl + "/status", cancellationToken);
            if (!statusResp.IsSuccessStatusCode)
                return new(false, "http_error", null, null, null, $"Bridge HTTP {(int)statusResp.StatusCode}");

            using var statusDoc = JsonDocument.Parse(await statusResp.Content.ReadAsStringAsync(cancellationToken));
            var root = statusDoc.RootElement;
            var state = root.TryGetProperty("state", out var s) ? (s.GetString() ?? "unknown") : "unknown";
            var phone = root.TryGetProperty("phone", out var p) ? p.GetString() : null;
            var name = root.TryGetProperty("displayName", out var n) ? n.GetString() : null;
            var qrAvail = root.TryGetProperty("qrAvailable", out var q) && q.ValueKind == JsonValueKind.True;

            // /qr — sadece QR varsa cek (gereksiz trafik olmasin)
            string? qrDataUrl = null;
            if (qrAvail)
            {
                var qrResp = await client.GetAsync(baseUrl + "/qr", cancellationToken);
                if (qrResp.IsSuccessStatusCode)
                {
                    using var qrDoc = JsonDocument.Parse(await qrResp.Content.ReadAsStringAsync(cancellationToken));
                    if (qrDoc.RootElement.TryGetProperty("qr", out var qrEl) && qrEl.ValueKind == JsonValueKind.String)
                        qrDataUrl = qrEl.GetString();
                }
            }

            // ready durumunda DisplayPhoneNumber'i senkronize tut, non-ready'de TEMIZLE.
            // Boylece "Aktif" rozeti telefon logout/disconnect sonrasi yapisip kalmaz.
            if (state == "ready" && !string.IsNullOrEmpty(phone))
            {
                var formatted = "+" + phone;
                if (cfg.DisplayPhoneNumber != formatted)
                {
                    cfg.DisplayPhoneNumber = formatted;
                    cfg.UpdatedAt = DateTime.UtcNow;
                    await _repo.SaveAsync(cfg, cancellationToken);
                }
            }
            else if (state != "ready" && !string.IsNullOrEmpty(cfg.DisplayPhoneNumber))
            {
                // Bridge ready degil ama DB'de eski phone duruyor → temizle
                cfg.DisplayPhoneNumber = null;
                cfg.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveAsync(cfg, cancellationToken);
            }

            return new(true, state, qrDataUrl, phone, name, null);
        }
        catch (TaskCanceledException)
        {
            await ClearDisplayPhoneIfSetAsync(cfg, cancellationToken);
            return new(false, "timeout", null, null, null, "Bridge zaman asimi (4sn).");
        }
        catch (HttpRequestException ex)
        {
            await ClearDisplayPhoneIfSetAsync(cfg, cancellationToken);
            return new(false, "unreachable", null, null, null, ex.Message);
        }
        catch (Exception ex)
        {
            return new(false, "error", null, null, null, ex.Message);
        }
    }

    private async Task ClearDisplayPhoneIfSetAsync(Domain.Entities.WhatsAppConfig cfg, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(cfg.DisplayPhoneNumber))
        {
            cfg.DisplayPhoneNumber = null;
            cfg.UpdatedAt = DateTime.UtcNow;
            try { await _repo.SaveAsync(cfg, ct); } catch { /* DB hatasi onemli degil bu kontekste */ }
        }
    }

    /// <summary>Web QR Node Bridge'i sağlık kontrolü — kısa timeout, açık hata mesajı.</summary>
    private async Task<WhatsAppTestResult> TestWebQrBridgeAsync(Domain.Entities.WhatsAppConfig cfg, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            return new(false, "Bridge URL bos. Default: http://localhost:61100", null, "error");

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5); // Kisa timeout — Bridge calisiyorsa hizli cevap verir

            var statusUrl = cfg.WebQrBridgeUrl.TrimEnd('/') + "/status";
            var resp = await client.GetAsync(statusUrl, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return new(false, $"Bridge HTTP {(int)resp.StatusCode}: {Truncate(body, 200)}", null, "error");

            using var doc = JsonDocument.Parse(body);
            var state = doc.RootElement.TryGetProperty("state", out var s) ? s.GetString() : "unknown";
            var phone = doc.RootElement.TryGetProperty("phone", out var p) ? p.GetString() : null;
            var name  = doc.RootElement.TryGetProperty("displayName", out var n) ? n.GetString() : null;

            cfg.LastError = null;
            cfg.UpdatedAt = DateTime.UtcNow;

            string msg;
            string kind;
            switch (state)
            {
                case "ready":
                    cfg.DisplayPhoneNumber = phone is null ? null : "+" + phone;
                    msg = $"Bridge hazir. {(name ?? "")} ({cfg.DisplayPhoneNumber})";
                    kind = "ok";
                    break;
                case "awaiting_qr":
                    msg = "Bridge calisiyor — telefondan QR taramasi bekleniyor.";
                    kind = "info";
                    break;
                case "connecting":
                    msg = "Bridge baglaniyor (~10-60sn surebilir, tekrar test et). Eski oturum geri yukleniyor olabilir.";
                    kind = "info";
                    break;
                default:
                    msg = $"Bridge state: {state}";
                    kind = "info";
                    break;
            }
            await _repo.SaveAsync(cfg, ct);
            return new(state == "ready", msg, cfg.DisplayPhoneNumber, kind);
        }
        catch (TaskCanceledException)
        {
            cfg.LastError = "Bridge zaman asimi (5sn). Bridge calisiyor mu? `npm start` ile baslat.";
            cfg.UpdatedAt = DateTime.UtcNow;
            await _repo.SaveAsync(cfg, ct);
            return new(false, cfg.LastError, null, "error");
        }
        catch (HttpRequestException ex)
        {
            cfg.LastError = $"Bridge'e baglanilamadi: {ex.Message}. Bridge calisiyor mu?";
            cfg.UpdatedAt = DateTime.UtcNow;
            await _repo.SaveAsync(cfg, ct);
            return new(false, cfg.LastError, null, "error");
        }
        catch (Exception ex)
        {
            return new(false, $"Beklenmeyen hata: {ex.Message}", null, "error");
        }
    }

    private static string NormalizePhone(string input)
        => WaPhoneNormalizer.Normalize(input) ?? string.Empty;

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s.Substring(0, max) + "...";
}
