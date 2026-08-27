using System.Linq;
using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
///   GET  /api/mobile/ping                     — sunucu dogrulama (anonim; urun adi + surum)
///   POST /api/mobile/login-companies          — email+parola dogrulanmis sirket listesi (login oncesi)
///   POST /api/mobile/login                    — cookie set + display name
///   POST /api/mobile/logout                   — cookie clear
///   GET  /api/mobile/whoami                   — oturum dogrulama (legacy, ok:false govde)
///   GET  /api/mobile/session                  — otomatik giris whoami (gercek 401 + kullanici/sirket restore)
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
    private readonly IPermissionService _permService;

    public MobileApiController(
        IUserAuthenticationService userAuth,
        ICompanyRepository companyRepo,
        CalibraHub.Application.Services.LoginLockoutTracker loginLockout,
        IPermissionService permService,
        ILogger<MobileApiController> logger)
    {
        _userAuth = userAuth;
        _companyRepo = companyRepo;
        _loginLockout = loginLockout;
        _permService = permService;
        _logger = logger;
    }

    /// <summary>
    /// Mobil uygulamanin menusunde yer alan ekranlarin form kodlari — /session yanitindaki
    /// `permissions` listesi BU kumeden suzulur. Mobil menu ekledikce buraya da eklenir;
    /// eksik birakilan kod istemcide "yetkisiz" gorunur (fail-safe yon: sizdirmaz, gizler).
    ///
    /// DIKKAT: bu liste yalniz MENU GORUNURLUGU icindir. Gercek yetki kontrolu her zaman
    /// ilgili endpoint'te (MobileWarehouseApiController / MobileProductionApiController
    /// icindeki RequirePermissionAsync) yapilir — buraya bir kod eklemek/cikarmak erisim
    /// vermez veya almaz, yalnizca karti gosterir/gizler.
    /// </summary>
    private static readonly string[] MobileMenuFormCodes =
    {
        CalibraHub.Application.Constants.FormCodes.StockIn,
        CalibraHub.Application.Constants.FormCodes.StockOut,
        CalibraHub.Application.Constants.FormCodes.Transfer,
        CalibraHub.Application.Constants.FormCodes.InventoryCount,
        CalibraHub.Application.Constants.FormCodes.SalesDelivery,
        CalibraHub.Application.Constants.FormCodes.PurchaseDelivery,
        CalibraHub.Application.Constants.FormCodes.WorkOrders,
        CalibraHub.Application.Constants.FormCodes.ShopFloor,
        CalibraHub.Application.Constants.FormCodes.ApprovalPending,
        CalibraHub.Application.Constants.FormCodes.Contacts,
        CalibraHub.Application.Constants.FormCodes.PurchaseRequest,
    };

    /// <summary>GET kapilariyla ayni aday aksiyonlar (PermissionEnforcementFilter GET seti).</summary>
    private static readonly string[] MenuViewActions = { "VIEW", "VIEW_OWN" };

    // 2026-08-24 (Y8): web login'deki hesap kilidi mobilde YOKTU — web'de 5 hatali
    // denemede kilitlenen hesap /api/mobile/login uzerinden kilitsiz denenebiliyordu
    // (kanal degistirerek kilit bypass'i). Ayni tracker, ayni e-posta anahtar uzayi.
    private readonly CalibraHub.Application.Services.LoginLockoutTracker _loginLockout;
    private readonly ILogger<MobileApiController> _logger;

    // ──────────────────────────────────────────────────────────────────────
    // Auth
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sunucu doğrulama — mobil istemci login'den ÖNCE "bu adres gerçekten bir
    /// CalibraHub sunucusu mu?" kontrolü yapar (base-URL yazım hatası / yanlış
    /// sunucu erken yakalanır). [AllowAnonymous] gerekçesi: kimlik gerektiren
    /// hiçbir veri dönmez — yalnız statik ürün adı + assembly sürümü; kurulum
    /// koruması gerektiren bir yüzey değildir (bkz. CLAUDE.md AllowAnonymous kuralı).
    /// </summary>
    [HttpGet("ping")]
    [AllowAnonymous]
    public IActionResult Ping()
        => Ok(new
        {
            ok = true,
            product = "CalibraHub",
            version = typeof(MobileApiController).Assembly.GetName().Version?.ToString(3) ?? "?",
        });

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
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

        var lockedUntil = _loginLockout.CheckLocked(req.Email);
        if (lockedUntil is not null)
        {
            var kalanDk = (int)Math.Ceiling((lockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            _logger.LogWarning("[MobileLogin] Kilitli hesaba deneme. Email={Email} KalanDk={Kalan}", req.Email, kalanDk);
            return Ok(new MobileLoginResponse(false, null,
                $"Cok fazla hatali deneme. {Math.Max(kalanDk, 1)} dakika sonra tekrar deneyin."));
        }

        var user = await _userAuth.AuthenticateAsync(req.Email, req.Password, companyId.Value, ct);
        if (user is null)
        {
            var nowLocked = _loginLockout.RegisterFailure(req.Email);
            _logger.LogWarning("[MobileLogin] Basarisiz giris. Email={Email} Kilitlendi={Locked}", req.Email, nowLocked);
            return Ok(new MobileLoginResponse(false, null, nowLocked
                ? "Cok fazla hatali deneme. Bir sure sonra tekrar deneyin."
                : "E-posta veya parola hatali."));
        }

        _loginLockout.Reset(req.Email);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new("company_id", user.CompanyId.ToString()),
            new("company_name", user.CompanyName)
        };
        // 2026-07-16: department_id claim — mobil endpoint'lerdeki merkezi yetki kontrolu
        // (IPermissionService, bkz. MobileWarehouseApiController.RequirePermissionAsync)
        // departman grant'larini web ile ayni cozebilsin diye. AccountController.Login ile
        // birebir ayni kosullu ekleme; login istek/yanit sozlesmesi degismez. NULL ise eklenmez.
        if (user.DepartmentId.HasValue)
            claims.Add(new Claim("department_id", user.DepartmentId.Value.ToString()));

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
    /// Yeni mobil login akışı: kullanıcı önce şirket seçmez — e-posta + parola girer,
    /// bu kimlik bilgileriyle GEÇERLİ olduğu (parolası doğrulanmış) şirketler listelenir.
    /// Kullanıcı ekranda listeden şirket seçer, ardından gerçek oturum
    /// POST /api/mobile/login {email,password,companyId} ile seçilen companyId verilerek açılır
    /// (bu endpoint session/cookie OLUŞTURMAZ — yalnızca aday şirket listesi döner).
    ///
    /// Not: Company + Users tabloları her zaman sistem (master) bağlantısında tutulur
    /// (bkz. SqlCompanyRepository / SqlUserProfileRepository → OpenSystemConnectionAsync),
    /// Users.CompanyId kolonuyla şirkete bağlanır. Bu yüzden per-company DB'lere tek tek
    /// bağlanmaya gerek yok — tek sorguda tüm şirketlerdeki eşleşen kullanıcı kayıtları gelir.
    /// Şirket sayısı arttıkça bu liste büyür (in-memory filtre); ileride şirket sayısı çok
    /// büyürse SQL tarafında email bazlı filtrelemeye taşınabilir (şu an GetAllAsync + LINQ,
    /// AccountController.GetCompanyOptionsByEmailAsync ile aynı desen).
    ///
    /// Güvenlik: parola doğrulanmadan (IPasswordHashService.VerifyPassword) HİÇBİR şirket
    /// dönülmez — email-only enumeration riskine karşı. Email bulunamadı / parola yanlış /
    /// kullanıcı pasif / hiç eşleşme yok — hepsinde aynı sonuç: boş liste []. Hangi kısmın
    /// hatalı olduğu asla ayrıştırılıp mesaj olarak dönülmez.
    /// </summary>
    [HttpPost("login-companies")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> LoginCompanies(
        [FromServices] IUserProfileRepository userProfileRepo,
        [FromServices] IPasswordHashService passwordHashService,
        [FromBody] MobileLoginCompaniesRequest? req,
        CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return Ok(Array.Empty<object>());

        var email = req.Email.Trim();
        var allUsers = await userProfileRepo.GetAllAsync(ct);

        // Aynı e-posta birden fazla şirkette ayrı Users kaydı olarak var olabilir
        // (UX_Users_Comp_Email = CompanyId+Email bileşik unique, global değil).
        // Parolası bu kayıtlardan HANGİLERİYLE eşleşiyorsa yalnız o şirketler adaydır.
        var matchedCompanyIds = allUsers
            .Where(u => u.IsActive
                        && u.CompanyId != 0
                        && string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase))
            .Where(u => passwordHashService.VerifyPassword(req.Password, u.PasswordHash))
            .Select(u => u.CompanyId)
            .Distinct()
            .ToHashSet();

        if (matchedCompanyIds.Count == 0)
            return Ok(Array.Empty<object>());

        var companies = await _companyRepo.GetAllAsync(ct);
        var result = companies
            .Where(c => c.IsActive && matchedCompanyIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .Select(c => new { id = c.Id, name = c.Name })
            .ToArray();

        return Ok(result);
    }

    /// <summary>
    /// Login ekranındaki şirket dropdown'ı için. Tek şirket varsa Android client
    /// dropdown'ı atlayıp direkt login yapar; çok şirketliyse kullanıcı seçer.
    /// </summary>
    [HttpGet("companies")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Companies(CancellationToken ct)
    {
        var companies = await _companyRepo.GetAllAsync(ct);
        return Ok(companies
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new { id = c.Id, name = c.Name }));
    }

    [HttpPost("logout")]
    [CalibraHub.Web.Authorization.PermissionGateReviewed("Izin gerekmez: yalniz cagiranin kendi oturumunu kapatir")]
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

    /// <summary>
    /// Mobil "otomatik giriş" (beni hatırla) whoami — uygulama açılışta kalıcı oturum
    /// çerezinin (Login yukarıda: IsPersistent + 30 gün) hâlâ geçerli olup olmadığını
    /// doğrular ve drawer başlığı için kullanıcı/şirket bilgisini restore eder.
    ///
    /// displayName / email / companyName BİREBİR Login'in (bu sınıf, ve web tarafında
    /// aynı desenle AccountController.Login) HttpContext.SignInAsync'e yazdığı claim'lerden
    /// okunur (ClaimTypes.Name / ClaimTypes.Email / "company_name") — ayrı bir DB sorgusu
    /// YOK. Böylece login sonrası mobilde gösterilen ad ile bu endpoint'ten dönen ad her
    /// zaman aynı kaynaktan gelir, asenkron sapma riski yoktur.
    ///
    /// [AllowAnonymous] + elle 401 tercihi (attribute [Authorize] yerine): bu projede
    /// AddCookie() için Events.OnRedirectToLogin override edilmemiş (bkz. Program.cs) —
    /// framework varsayılanı, kimliksiz istekte challenge tetiklenince "/Account/Login"a
    /// 302 REDIRECT üretir; mobil client için "302 + HTML login sayfası" ile gerçek 401
    /// ayırt edilmesi güvenilir değildir. Burada action'ı [AllowAnonymous] ile challenge
    /// pipeline'ının dışında tutup elle User.Identity.IsAuthenticated kontrolü + doğrudan
    /// Unauthorized() dönmek, framework detayından bağımsız garanti bir HTTP 401 sağlar
    /// (mobil sözleşmesi: HTTP 401 durum kodu, {ok:false} gövdesi DEĞİL).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("session")]
    public async Task<IActionResult> Session(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;
        var companyId = int.TryParse(User.FindFirstValue("company_id"), out var cid) ? cid : 0;

        return Ok(new
        {
            ok = true,
            userId,
            displayName = User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            companyId,
            companyName = User.FindFirstValue("company_name") ?? string.Empty,
            permissions = await GetMenuPermissionsAsync(ct),
        });
    }

    /// <summary>
    /// Kullanicinin GOREBILECEGI mobil ekranlarin form kodlari — mobil menu/ana ekran
    /// kartlarini suzmek icin. Web'de menu gorunurlugu zaten sunucu tarafinda suzuluyor;
    /// mobilde bu bilgi hic gonderilmedigi icin yetkisiz kullaniciya kart gosterilip
    /// dokununca 403 aliniyordu (kapali kapiya goturen buton).
    ///
    /// Rol/departman claim cozumu MobileWarehouseApiController.GetCurrentUser ile BIREBIR
    /// ayni (TryParseRole; bilinmeyen rol -> Operator fail-safe). SystemAdmin/DepartmentManager
    /// bypass'lari PermissionService icindedir, burada tekrarlanmaz.
    ///
    /// Hata durumunda BOS liste DEGIL, tam katalog doner (fail-open): izin sorgusu gecici
    /// olarak patlarsa kullanici bos bir menuyle kilitlenmemeli — sunucu tarafi asil kapi
    /// zaten endpoint'lerde, menu yalnizca gorunurluk katmani.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetMenuPermissionsAsync(CancellationToken ct)
    {
        try
        {
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);

            var roleStr = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            if (!CalibraHub.Application.Security.UserAuthorizationCatalog.TryParseRole(roleStr, out var role))
                role = CalibraHub.Domain.Enums.UserRole.Operator;

            int? departmentId = int.TryParse(User.FindFirstValue("department_id"), out var d) && d > 0 ? d : null;

            var allowed = new List<string>(MobileMenuFormCodes.Length);
            foreach (var formCode in MobileMenuFormCodes)
            {
                if (await _permService.CheckAnyAsync(userId, role, departmentId, formCode, MenuViewActions, ct))
                    allowed.Add(formCode);
            }
            return allowed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mobil menu izinleri cozulemedi — tam katalog donuluyor (fail-open).");
            return MobileMenuFormCodes;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // WhatsApp endpoints — mevcut /Whatsapp/* ile aynı veri, JSON formatı.
    // ──────────────────────────────────────────────────────────────────────

    [Authorize]
    [CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.WhatsApp)]
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
    [CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.WhatsApp)]
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
    [CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.WhatsApp)]
    [HttpPost("whatsapp/send")]
    public async Task<IActionResult> SendText(
        [FromServices] IWhatsAppService whatsApp,
        [FromServices] IWaInboxRepository inbox,
        [FromServices] IWhatsAppRealTimeNotifier notifier,
        [FromBody] MobileSendTextRequest req,
        CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Phone) || string.IsNullOrWhiteSpace(req.Text))
            return Ok(new MobileSendResponse(false, null, "Telefon ve metin zorunlu."));

        // interactive=true: mobile composer'dan elle yazilan mesaj — anti-spam
        // human-delay atlanir (web /Whatsapp/Send ile ayni semantik).
        var result = await whatsApp.SendTextMessageAsync(req.Phone, req.Text, ct, interactive: true);

        if (result.Success)
        {
            var normalized = NormalizePhone(req.Phone);
            var msgId = result.MessageId ?? $"local-{DateTime.UtcNow.Ticks}";
            var now = DateTime.UtcNow;
            try
            {
                await inbox.InsertIfNotExistsAsync(new WaInboxMessage
                {
                    BridgeMsgId  = msgId,
                    Direction    = 1,
                    ContactPhone = normalized,
                    Body         = req.Text,
                    MediaType    = "chat",
                    HasMedia     = false,
                    ReceivedAt   = now,
                    CreatedAt    = now,
                }, ct);
                await notifier.MessageReceivedAsync(normalized, msgId, 1, req.Text, "chat", false,
                    null, null, null, null, now, ct);
                await notifier.ConversationUpdatedAsync(normalized, ct);
            }
            catch { /* SignalR/insert hataları gönderimi engellememeli */ }
        }

        return Ok(new MobileSendResponse(result.Success, result.MessageId, result.Success ? null : result.Message));
    }

    [Authorize]
    [CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.WhatsApp)]
    [HttpPost("whatsapp/send-media")]
    [RequestSizeLimit(60_000_000)]   // 60 MB — web ile ayni
    public async Task<IActionResult> SendMedia(
        [FromServices] IWhatsAppConfigRepository configRepo,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IWaInboxRepository inbox,
        [FromServices] IWhatsAppRealTimeNotifier notifier,
        [FromServices] IWebHostEnvironment env,
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

            var httpReq = new HttpRequestMessage(HttpMethod.Post,
                cfg.WebQrBridgeUrl.TrimEnd('/') + "/send-media");
            var normalized = NormalizePhone(phone);
            httpReq.Headers.TryAddWithoutValidation("X-To", normalized);
            if (!string.IsNullOrWhiteSpace(caption))
                httpReq.Headers.TryAddWithoutValidation("X-Caption", Uri.EscapeDataString(caption));
            if (!string.IsNullOrWhiteSpace(file.FileName))
                httpReq.Headers.TryAddWithoutValidation("X-Filename", Uri.EscapeDataString(file.FileName));

            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                file.ContentType ?? "application/octet-stream");
            httpReq.Content = content;

            using var resp = await client.SendAsync(httpReq, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var o) && o.ValueKind == System.Text.Json.JsonValueKind.True;
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            var msgId = root.TryGetProperty("messageId", out var mid) ? mid.GetString() : null;

            if (ok)
            {
                var now = DateTime.UtcNow;
                msgId ??= $"local-{now.Ticks}";
                var mime = file.ContentType ?? "application/octet-stream";
                var mediaType = mime.StartsWith("image/")  ? "image"
                              : mime.StartsWith("video/")  ? "video"
                              : mime.StartsWith("audio/")  ? "audio"
                              : "document";
                // GUVENLIK (2026-08-24, Y4): uzanti istemcinin dosya adindan DEGIL MIME
                // allowlist'inden gelir (".html" ile wwwroot'a yazip ayni origin'de
                // calistirma yolu kapatildi). Nokta basta olacak sekilde normalize edilir.
                var ext = "." + CalibraHub.Application.Services.Messaging.WaMediaFiles.MimeToExtension(mime);
                var yyyy = now.Year.ToString("D4");
                var mm   = now.Month.ToString("D2");
                string? urlPath = null;
                try
                {
                    var uploadsDir = Path.Combine(env.WebRootPath, "uploads", "whatsapp", yyyy, mm);
                    Directory.CreateDirectory(uploadsDir);
                    var safeMsgId = CalibraHub.Application.Services.Messaging.WaMediaFiles.SafeId(msgId);
                    var diskPath = Path.Combine(uploadsDir, $"{safeMsgId}{ext}");
                    await System.IO.File.WriteAllBytesAsync(diskPath, bytes, ct);
                    urlPath = $"/uploads/whatsapp/{yyyy}/{mm}/{safeMsgId}{ext}";
                }
                catch { /* dosya kaydetme başarısız olursa mediaUrl olmadan devam */ }

                try
                {
                    await inbox.InsertIfNotExistsAsync(new WaInboxMessage
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
                    }, ct);
                    await notifier.MessageReceivedAsync(normalized, msgId, 1, caption, mediaType, true,
                        urlPath, mime, file.FileName, bytes.Length, now, ct);
                    await notifier.ConversationUpdatedAsync(normalized, ct);
                }
                catch { /* SignalR/insert hataları gönderimi engellememeli */ }
            }

            return Ok(new MobileSendResponse(ok, msgId, error));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MobileApi] SendMedia başarısız.");
            return Ok(new MobileSendResponse(false, null, "Bridge hatasi: Islem sirasinda bir hata olustu."));
        }
    }

    [Authorize]
    [HttpPost("whatsapp/mark-read")]
    [CalibraHub.Web.Authorization.PermissionGateReviewed("Izin gerekmez: WhatsApp gelen kutusu sirket geneli paylasimlidir")]
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
public sealed record MobileLoginCompaniesRequest(string Email, string Password);
public sealed record MobileSendTextRequest(string Phone, string Text);
public sealed record MobileSendResponse(bool Ok, string? MessageId, string? Error);
