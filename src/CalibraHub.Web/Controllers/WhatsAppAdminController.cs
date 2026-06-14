using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// WhatsAppAdminController — Sirket Ayarlari > WhatsApp sekmesinin POST/GET
/// endpoint'leri (rapor §2.3 AdminController split).
///
/// Tasinmis endpoint'ler:
///   - POST /Admin/WhatsApp/Save           → config + safety rules upsert
///   - POST /Admin/WhatsApp/Test           → cloud API erisim testi
///   - POST /Admin/WhatsApp/SendTest       → tek mesaj testi
///   - POST /Admin/WhatsApp/PairingCode    → bridge'den 8 haneli kod iste
///   - GET  /Admin/WhatsApp/Qr             → web qr durumu polling
///
/// AdminController'da kalan: CompanySettings view (WhatsApp ayarlari da burada
/// gosteriliyor — view ViewBag uzerinden config'i okur).
/// </summary>
[Authorize]
[PermissionScope(FormCodes.CompanySettings)]
public sealed class WhatsAppAdminController : Controller
{
    [HttpPost("/Admin/WhatsApp/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveWhatsApp(
        [FromServices] IWhatsAppService whatsAppService,
        int provider, string? accessToken, string? phoneNumberId,
        string? businessAccountId, string? webhookVerifyToken,
        string? webQrBridgeUrl, bool isEnabled, CancellationToken ct)
    {
        // Provider seçimi: 0=CloudApi, 1=WebQr
        var existing = await whatsAppService.GetConfigAsync(ct);
        var providerType = (CalibraHub.Domain.Entities.WhatsAppProviderType)provider;

        var configRepo = HttpContext.RequestServices.GetRequiredService<IWhatsAppConfigRepository>();
        var cfg = await configRepo.GetAsync(ct) ?? new CalibraHub.Domain.Entities.WhatsAppConfig
        {
            Id = 1,
            CreatedAt = DateTime.UtcNow,
        };
        cfg.Provider           = providerType;
        cfg.PhoneNumberId      = phoneNumberId?.Trim();
        cfg.BusinessAccountId  = businessAccountId?.Trim();
        cfg.WebhookVerifyToken = webhookVerifyToken?.Trim();
        cfg.WebQrBridgeUrl     = webQrBridgeUrl?.Trim();
        cfg.IsEnabled          = isEnabled;
        cfg.UpdatedAt          = DateTime.UtcNow;
        if (cfg.CreatedAt == default) cfg.CreatedAt = DateTime.UtcNow;

        // Yeni token gelirse DPAPI ile şifrele
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            try { cfg.AccessTokenEncrypted = CalibraHub.Application.Security.DpapiSecretDecryptor.Encrypt(accessToken.Trim()); }
            catch { cfg.AccessTokenEncrypted = accessToken.Trim(); }
        }
        // Cloud API seçilip token henüz yoksa hata
        if (providerType == CalibraHub.Domain.Entities.WhatsAppProviderType.CloudApi
            && string.IsNullOrEmpty(cfg.AccessTokenEncrypted))
        {
            return Json(new { success = false, message = "Cloud API için Access Token zorunlu." });
        }

        await configRepo.SaveAsync(cfg, ct);

        return Json(new { success = true, message = "Yapılandırma kaydedildi." });
    }

    [HttpPost("/Admin/WhatsApp/Test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestWhatsApp([FromServices] IWhatsAppService whatsAppService, CancellationToken ct)
    {
        var result = await whatsAppService.TestConfigAsync(ct);
        return Json(new
        {
            success      = result.Success,
            kind         = result.Kind,
            message      = result.Message,
            displayPhone = result.DisplayPhoneNumber,
        });
    }

    [HttpPost("/Admin/WhatsApp/SendTest")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendWhatsAppTest(
        [FromServices] IWhatsAppService whatsAppService,
        string toPhone, string message, CancellationToken ct)
    {
        // 2026-05-23 fix: Şirket Ayarları "Test Mesajı Gönder" butonu kullanıcının elle
        // tetiklediği manuel bir çağrı — insan-benzeri 3-15sn rastgele gecikme uygulanmasın
        // (interactive=true). Aksi halde admin test ederken sebepsiz bekletme yaşar.
        var result = await whatsAppService.SendTextMessageAsync(toPhone ?? "", message ?? "", ct, interactive: true);
        return Json(new { success = result.Success, message = result.Message, messageId = result.MessageId });
    }

    /// <summary>Bridge'den 8 haneli pairing code iste — telefonla baglanmak icin alternatif yol.</summary>
    [HttpPost("/Admin/WhatsApp/PairingCode")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetWhatsAppPairingCode(
        [FromServices] IWhatsAppConfigRepository configRepo,
        [FromServices] IHttpClientFactory httpClientFactory,
        string phone, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return Json(new { success = false, message = "Telefon zorunlu (orn: 905338168150)" });

        var cfg = await configRepo.GetAsync(ct);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            return Json(new { success = false, message = "Bridge URL ayarlanmamis." });

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var resp = await client.PostAsJsonAsync(
                cfg.WebQrBridgeUrl.TrimEnd('/') + "/pairing-code",
                new { phone },
                ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var o) && o.ValueKind == System.Text.Json.JsonValueKind.True;
            var code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            return Json(new { success = ok, code, message = error ?? "Pairing code uretildi." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Bridge hatasi: {ex.Message}" });
        }
    }

    /// <summary>Web QR sayfasi icin polling ucu — Bridge'in /status+/qr cevabini birlestirip frontend'e doner.</summary>
    [HttpGet("/Admin/WhatsApp/Qr")]
    public async Task<IActionResult> GetWhatsAppQr([FromServices] IWhatsAppService whatsAppService, CancellationToken ct)
    {
        var r = await whatsAppService.GetWebQrStatusAsync(ct);
        return Json(new
        {
            reachable   = r.Reachable,
            state       = r.State,
            qr          = r.Qr,
            phone       = r.Phone,
            displayName = r.DisplayName,
            error       = r.Error,
        });
    }
}
