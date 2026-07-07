using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Services.Messaging;
using CalibraHub.Application.WhatsApp;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// WhatsApp Bridge'in ANLIK event push hedefi.
/// Bridge (Node/Baileys, ayni makinede calisan Windows servisi) her onemli olayda
/// buraya POST atar; boylece mesajlar/tikler/presence 60sn polling'i beklemeden
/// gercek zamanli islenir. Polling yedek katman olarak calismaya devam eder —
/// dedup UNIQUE bridge_msg_id ile saglanir.
///
/// Guvenlik: Bridge her zaman ayni sunucuda kosar → yalnizca localhost kabul edilir
/// (WhatsAppWebhookController'in WebQr dali ile ayni kural).
///
/// Payload: { "events": [ { "type": "message"|"receipt"|"presence"|"revoke", ... } ] }
///  - message : BridgeInboundMessage schema'si (GET /messages ile ayni)
///  - receipt : { messageId, phone, status: "sent"|"delivered"|"read" }
///  - presence: { phone, status: "typing"|"paused"|"online"|"offline", lastSeen? }
///  - revoke  : { messageId, phone }
/// </summary>
[ApiController]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("api/whatsapp/bridge-events")]
public sealed class WhatsAppBridgeEventsController : ControllerBase
{
    private readonly IWhatsAppConfigRepository _cfgRepo;
    private readonly WhatsAppInboundProcessor _processor;
    private readonly CompanyConnectionRegistry _registry;
    private readonly ILogger<WhatsAppBridgeEventsController> _logger;

    // Şirket çözümü cache'i: 0 = sistem DB (tek-DB kurulum), >0 = per-company DB.
    // Bridge push'u anonim geldiğinden istekte şirket bilgisi yoktur; WebQr config'i
    // hangi DB'de bulunursa o şirket 5 dk cache'lenir.
    private static int? _cachedCompanyId;
    private static DateTime _cachedAt;
    private static readonly TimeSpan CompanyCacheTtl = TimeSpan.FromMinutes(5);

    public WhatsAppBridgeEventsController(
        IWhatsAppConfigRepository cfgRepo,
        WhatsAppInboundProcessor processor,
        CompanyConnectionRegistry registry,
        ILogger<WhatsAppBridgeEventsController> logger)
    {
        _cfgRepo   = cfgRepo;
        _processor = processor;
        _registry  = registry;
        _logger    = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // ── Yalnizca localhost — bridge ayni makinede kosar ──────────────
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        var isLocalhost = remoteIp is not null
            && (remoteIp.Equals(System.Net.IPAddress.Loopback)
                || remoteIp.Equals(System.Net.IPAddress.IPv6Loopback)
                || remoteIp.ToString() == "127.0.0.1");
        if (!isLocalhost)
        {
            _logger.LogWarning("[WaBridgeEvents] localhost dışından istek reddedildi — IP: {ip}", remoteIp);
            return Forbid();
        }

        // ── Şirket çözümü — WebQr config'i hangi DB'de? ──────────────────
        // Anonim istekte company claim yok; sistem DB'yi ve kayıtlı şirket
        // DB'lerini tarayarak WebQr aktif config'i bul, o şirket bağlamında işle.
        var (cfg, diag) = await ResolveWebQrConfigAsync(ct);
        if (cfg is null)
        {
            return Ok(new { ok = false, note = "WebQr provider aktif değil", diag });
        }
        var bridgeBase = cfg.WebQrBridgeUrl!.TrimEnd('/');

        JsonDocument doc;
        try
        {
            using var reader = new StreamReader(Request.Body);
            var raw = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(raw)) return Ok(new { ok = true, processed = 0 });
            doc = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            return BadRequest(new { ok = false, error = "geçersiz JSON" });
        }

        using var _doc = doc;
        if (!doc.RootElement.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            return Ok(new { ok = true, processed = 0 });

        var processed = 0;
        foreach (var el in events.EnumerateArray())
        {
            try
            {
                var type = el.TryGetProperty("type", out var t) ? t.GetString() : null;
                switch (type)
                {
                    case "message":
                    {
                        var m = el.Deserialize<BridgeInboundMessage>();
                        if (m is not null)
                        {
                            await _processor.ProcessMessageAsync(m, bridgeBase, ct);
                            processed++;
                        }
                        break;
                    }
                    case "receipt":
                    {
                        var messageId = GetString(el, "messageId");
                        var phone     = GetString(el, "phone");
                        var status    = GetString(el, "status");
                        if (messageId is not null && phone is not null && status is not null)
                        {
                            await _processor.ProcessReceiptAsync(messageId, phone, status, ct);
                            processed++;
                        }
                        break;
                    }
                    case "presence":
                    {
                        var phone  = GetString(el, "phone");
                        var status = GetString(el, "status");
                        DateTime? lastSeen = null;
                        if (el.TryGetProperty("lastSeen", out var ls) && ls.ValueKind == JsonValueKind.String
                            && DateTime.TryParse(ls.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
                        {
                            lastSeen = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                        }
                        if (phone is not null && status is not null)
                        {
                            await _processor.ProcessPresenceAsync(phone, status, lastSeen, ct);
                            processed++;
                        }
                        break;
                    }
                    case "revoke":
                    {
                        var messageId = GetString(el, "messageId");
                        var phone     = GetString(el, "phone");
                        if (messageId is not null && phone is not null)
                        {
                            await _processor.ProcessRevokeAsync(messageId, phone, ct);
                            processed++;
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WaBridgeEvents] event işlenemedi");
            }
        }

        return Ok(new { ok = true, processed });
    }

    private static string? GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// WebQr config'ini bulur: önce cache, sonra sistem DB (override'sız), sonra
    /// kayıtlı şirket DB'leri. Bulunan şirket HttpContext.Items override'ına yazılır —
    /// böylece processor'ın tüm repository çağrıları doğru şirket DB'sine gider.
    /// </summary>
    private async Task<(WhatsAppConfig? Cfg, string Diag)> ResolveWebQrConfigAsync(CancellationToken ct)
    {
        static bool IsUsable(WhatsAppConfig? c) =>
            c is { IsEnabled: true, Provider: WhatsAppProviderType.WebQr }
            && !string.IsNullOrWhiteSpace(c.WebQrBridgeUrl);

        static string Describe(WhatsAppConfig? c) =>
            c is null ? "null" : $"prov={c.Provider} on={c.IsEnabled} url={(string.IsNullOrWhiteSpace(c.WebQrBridgeUrl) ? "-" : "+")}";

        var diag = new System.Text.StringBuilder();

        // 1) Cache
        if (_cachedCompanyId.HasValue && DateTime.UtcNow - _cachedAt < CompanyCacheTtl)
        {
            if (_cachedCompanyId.Value > 0)
                HttpContext.Items["__override_company_id"] = _cachedCompanyId.Value;
            try
            {
                var cached = await _cfgRepo.GetAsync(ct);
                if (IsUsable(cached)) return (cached, "cache");
            }
            catch { /* cache bayat — yeniden tara */ }
            HttpContext.Items.Remove("__override_company_id");
            _cachedCompanyId = null;
        }

        // 2) Sistem DB (tek-DB kurulumlar — şirket dedicated DB kullanmıyorsa)
        try
        {
            var sysCfg = await _cfgRepo.GetAsync(ct);
            diag.Append("sys:").Append(Describe(sysCfg));
            if (IsUsable(sysCfg))
            {
                _cachedCompanyId = 0;
                _cachedAt = DateTime.UtcNow;
                return (sysCfg, "sys");
            }
        }
        catch (Exception ex) { diag.Append("sys:EX(").Append(ex.Message.Split('\n')[0]).Append(')'); }

        // 3) Kayıtlı şirket DB'lerini tara
        var companyIds = _registry.GetCompanyIds();
        diag.Append(" companies:[").Append(string.Join(',', companyIds)).Append(']');
        foreach (var companyId in companyIds)
        {
            HttpContext.Items["__override_company_id"] = companyId;
            try
            {
                var companyCfg = await _cfgRepo.GetAsync(ct);
                diag.Append(" c").Append(companyId).Append(':').Append(Describe(companyCfg));
                if (IsUsable(companyCfg))
                {
                    _cachedCompanyId = companyId;
                    _cachedAt = DateTime.UtcNow;
                    _logger.LogInformation("[WaBridgeEvents] WebQr config şirket {id} DB'sinde bulundu.", companyId);
                    return (companyCfg, $"company:{companyId}"); // override yerinde kalır
                }
            }
            catch (Exception ex)
            {
                diag.Append(" c").Append(companyId).Append(":EX(").Append(ex.Message.Split('\n')[0]).Append(')');
            }
        }

        HttpContext.Items.Remove("__override_company_id");
        return (null, diag.ToString());
    }
}
