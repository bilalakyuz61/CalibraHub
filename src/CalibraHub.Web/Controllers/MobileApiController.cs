using System.Linq;
using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Android companion app icin JSON-only REST surface.
///
/// MVC /Whatsapp/* controller'i Web UI'a ozgu (AntiForgery + Razor view). Mobile
/// client'lar ayrı bir rotadan calisir — cookie-based auth korunur, fakat CSRF
/// token yerine `X-Requested-With: CalibraHubAndroid` header'i ile origin
/// dogrulanir. Bu, AVD emulator ve fiziksel cihaz dev cycle'ini kolaylastirir.
///
/// Endpoint'ler:
///   POST /api/mobile/login                    — cookie set + display name
///   POST /api/mobile/logout                   — cookie clear
///   GET  /api/mobile/whoami                   — oturum dogrulama
///   GET  /api/mobile/whatsapp/conversations   — sohbet listesi (sidebar)
///   GET  /api/mobile/whatsapp/messages        — bir sohbetin mesajlari
///   POST /api/mobile/whatsapp/send            — metin gonder
///   POST /api/mobile/whatsapp/send-media      — dosya gonder (multipart)
///   POST /api/mobile/whatsapp/mark-read       — sohbeti okundu isaretle
/// </summary>
[ApiController]
[Route("api/mobile")]
[IgnoreAntiforgeryToken]
[EnableCors("MobileApi")]
public sealed class MobileApiController : ControllerBase
{
    private const int ConversationLimit = 200;
    private const int MessagesPerConversationLimit = 200;

    private readonly IUserAuthenticationService _userAuth;
    private readonly ICompanyRepository _companyRepo;

    public MobileApiController(
        IUserAuthenticationService userAuth,
        ICompanyRepository companyRepo)
    {
        _userAuth = userAuth;
        _companyRepo = companyRepo;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Auth
    // ──────────────────────────────────────────────────────────────────────

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] MobileLoginRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return Ok(new MobileLoginResponse(false, null, "E-posta ve parola zorunlu."));

        // Mobile MVP: companyId optional. Verilmemisse, kullanicinin tek sirketi
        // varsa onu kullan; birden cok sirkete bagliysa hata don.
        int? companyId = req.CompanyId;
        if (!companyId.HasValue)
        {
            var companies = await _companyRepo.GetAllAsync(ct);
            if (companies.Count == 1) companyId = companies.First().Id;
            else if (companies.Count == 0)
                return Ok(new MobileLoginResponse(false, null, "Sistemde tanimli sirket yok."));
            else
                return Ok(new MobileLoginResponse(false, null,
                    "Birden cok sirket var; companyId alanini doldurun."));
        }

        var user = await _userAuth.AuthenticateAsync(req.Email, req.Password, companyId.Value, ct);
        if (user is null)
            return Ok(new MobileLoginResponse(false, null, "E-posta veya parola hatali."));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new("company_id", user.CompanyId.ToString()),
            new("company_name", user.CompanyName)
        };

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        var props = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)   // mobile: uzun oturum
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal, props);

        return Ok(new MobileLoginResponse(true, user.FullName, null));
    }

    /// <summary>
    /// Login ekranındaki şirket dropdown'ı için. Tek şirket varsa Android client
    /// dropdown'ı atlayıp direkt login yapar; çok şirketliyse kullanıcı seçer.
    /// </summary>
    [HttpGet("companies")]
    [AllowAnonymous]
    public async Task<IActionResult> Companies(CancellationToken ct)
    {
        var companies = await _companyRepo.GetAllAsync(ct);
        return Ok(companies
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new { id = c.Id, name = c.Name }));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { ok = true });
    }

    [HttpGet("whoami")]
    public IActionResult WhoAmI()
    {
        var authenticated = User.Identity?.IsAuthenticated ?? false;
        return Ok(new
        {
            ok = authenticated,
            userName = authenticated ? User.Identity?.Name : null
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // WhatsApp endpoints — mevcut /Whatsapp/* ile aynı veri, JSON formatı.
    // ──────────────────────────────────────────────────────────────────────

    [Authorize]
    [HttpGet("whatsapp/conversations")]
    public async Task<IActionResult> Conversations(
        [FromServices] IWaInboxRepository inbox,
        [FromQuery] int limit = ConversationLimit,
        CancellationToken ct = default)
    {
        var convs = await inbox.GetConversationsAsync(Math.Clamp(limit, 1, 500), ct);
        return Ok(convs.Select(c => new
        {
            phone           = c.ContactPhone,
            displayName     = c.WaName ?? c.AccountTitle ?? c.ContactName ?? c.ContactPhone,
            contactCode     = c.AccountCode,
            lastMessage     = c.LastBody,
            lastMessageAt   = DateTime.SpecifyKind(c.LastAt, DateTimeKind.Utc).ToString("o"),
            unreadCount     = c.UnreadCount,
            lastMediaType   = c.LastMediaType,
        }));
    }

    [Authorize]
    [HttpGet("whatsapp/messages")]
    public async Task<IActionResult> Messages(
        [FromServices] IWaInboxRepository inbox,
        [FromQuery] string phone,
        [FromQuery] int limit = MessagesPerConversationLimit,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest(new { error = "phone zorunlu" });

        var normalized = NormalizePhone(phone);
        var msgs = await inbox.GetMessagesByPhoneAsync(normalized, Math.Clamp(limit, 1, 500), ct);
        return Ok(msgs.Select(m => new
        {
            id              = m.Id,
            direction       = m.Direction,
            body            = m.Body,
            mediaType       = m.MediaType,
            mediaPath       = m.MediaPath,
            mediaMime       = m.MediaMime,
            mediaFilename   = m.MediaFileName,
            mediaSize       = m.MediaSize,
            receivedAt      = DateTime.SpecifyKind(m.ReceivedAt, DateTimeKind.Utc).ToString("o"),
        }));
    }

    [Authorize]
    [HttpPost("whatsapp/send")]
    public async Task<IActionResult> SendText(
        [FromServices] IWhatsAppService whatsApp,
        [FromBody] MobileSendTextRequest req,
        CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Phone) || string.IsNullOrWhiteSpace(req.Text))
            return Ok(new MobileSendResponse(false, null, "Telefon ve metin zorunlu."));

        // interactive=true: mobile composer'dan elle yazilan mesaj — anti-spam
        // human-delay atlanir (web /Whatsapp/Send ile ayni semantik).
        var result = await whatsApp.SendTextMessageAsync(req.Phone, req.Text, ct, interactive: true);
        return Ok(new MobileSendResponse(result.Success, result.MessageId, result.Success ? null : result.Message));
    }

    [Authorize]
    [HttpPost("whatsapp/send-media")]
    [RequestSizeLimit(60_000_000)]   // 60 MB — web ile ayni
    public async Task<IActionResult> SendMedia(
        [FromServices] IWhatsAppConfigRepository configRepo,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromForm] string phone,
        [FromForm] string? caption,
        IFormFile file,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone) || file is null || file.Length == 0)
            return Ok(new MobileSendResponse(false, null, "Telefon ve dosya zorunlu."));

        var cfg = await configRepo.GetAsync(ct);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.WebQrBridgeUrl))
            return Ok(new MobileSendResponse(false, null, "Bridge URL ayarlanmamis."));

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            var req = new HttpRequestMessage(HttpMethod.Post,
                cfg.WebQrBridgeUrl.TrimEnd('/') + "/send-media");
            req.Headers.TryAddWithoutValidation("X-To", NormalizePhone(phone));
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
            var msgId = root.TryGetProperty("messageId", out var mid) ? mid.GetString() : null;
            return Ok(new MobileSendResponse(ok, msgId, error));
        }
        catch (Exception ex)
        {
            return Ok(new MobileSendResponse(false, null, $"Bridge hatasi: {ex.Message}"));
        }
    }

    [Authorize]
    [HttpPost("whatsapp/mark-read")]
    public async Task<IActionResult> MarkRead(
        [FromServices] IWaInboxRepository inbox,
        [FromQuery] string phone,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest(new { error = "phone zorunlu" });

        var normalized = NormalizePhone(phone);
        var updated = await inbox.MarkConversationReadAsync(normalized, DateTime.UtcNow, ct);
        return Ok(new { ok = true, updated });
    }

    private static string NormalizePhone(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input.Trim())
            if (char.IsDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}

// ──────────────────────────────────────────────────────────────────────────
// DTO'lar — Android Retrofit DTO'lari (mobile/CalibraHubAndroid/.../CalibraApi.kt)
// ile birebir eslesir. Yapı değişirse iki tarafı birlikte güncelle.
// ──────────────────────────────────────────────────────────────────────────

public sealed record MobileLoginRequest(string Email, string Password, int? CompanyId = null);
public sealed record MobileLoginResponse(bool Ok, string? DisplayName, string? Error);
public sealed record MobileSendTextRequest(string Phone, string Text);
public sealed record MobileSendResponse(bool Ok, string? MessageId, string? Error);
