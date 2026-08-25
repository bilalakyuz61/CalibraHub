using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Web.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Sistem Ayarlari gate'i — admin sifresi ile kilit acilir (PBKDF2-hashed, DB'de saklanir).
/// Basari sonrasi Session["GateUnlockedAt"] set edilir; [GateProtected] action'lar buna bakar.
/// İcerideki sekmeler: Lisans Yonetimi, Veri Tabani Ayarlari, Sifre Degistirme.
/// </summary>
[AllowAnonymous]
public sealed class GateController : Controller
{
    private readonly IGatePasswordService _passwordService;
    private readonly ILicenseService      _licenseService;
    private readonly IMachineIdProvider   _machineIdProvider;
    // 2026-08-24 K2: gate kaba-kuvvet kilidi (login ile AYNI tracker, ayri anahtar uzayi).
    private readonly CalibraHub.Application.Services.LoginLockoutTracker _lockout;
    private readonly ILogger<GateController> _logger;

    public GateController(
        IGatePasswordService passwordService,
        ILicenseService licenseService,
        IMachineIdProvider machineIdProvider,
        CalibraHub.Application.Services.LoginLockoutTracker lockout,
        ILogger<GateController> logger)
    {
        _passwordService   = passwordService;
        _licenseService    = licenseService;
        _machineIdProvider = machineIdProvider;
        _lockout           = lockout;
        _logger            = logger;
    }

    /// <summary>Kilit anahtari — gate tek paylasimli sifre oldugu icin kullanici yok; IP bazli.</summary>
    private string LockoutKey()
        => "gate:" + (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    // ── Gate Login ──────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Index(string? returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    // 2026-08-24 (K2 guvenlik denetimi): bu uc ANONIM ve arkasindaki [GateProtected]
    // ekranlar DB baglanti ayarlari + SystemAdmin kullanici olusturmayi aciyor. Eskiden tek
    // savunma 1 sn'lik Task.Delay idi — istek BASINA oldugu icin paralel baglantilarla
    // etkisizdi (dakikada binlerce deneme). Artik iki katman var: IP basina rate limit
    // ("auth" politikasi, Login ile ayni) + LoginLockoutTracker ile kalici kilit.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Verify(string password, string? returnUrl, CancellationToken ct)
    {
        var key = LockoutKey();
        var lockedUntil = _lockout.CheckLocked(key);
        if (lockedUntil is not null)
        {
            var kalan = (int)Math.Ceiling((lockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            _logger.LogWarning("[Gate] Kilitli IP'den deneme. Ip={Ip} KalanDk={Kalan}",
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "-", kalan);
            TempData["GateError"] = $"Cok fazla hatali deneme. {Math.Max(kalan, 1)} dakika sonra tekrar deneyin.";
            ViewBag.ReturnUrl = returnUrl;
            return View(nameof(Index));
        }

        if (string.IsNullOrEmpty(password) || !await _passwordService.VerifyAsync(password, ct))
        {
            var locked = _lockout.RegisterFailure(key);
            _logger.LogWarning("[Gate] Gecersiz sifre denemesi. Ip={Ip} Kilitlendi={Locked}",
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "-", locked);
            await Task.Delay(1000, ct);
            TempData["GateError"] = locked
                ? "Cok fazla hatali deneme. Bir sure sonra tekrar deneyin."
                : "Gecersiz sifre.";
            ViewBag.ReturnUrl = returnUrl;
            return View(nameof(Index));
        }

        _lockout.Reset(key);

        HttpContext.Session.SetString("GateUnlockedAt",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(Dashboard));
    }

    /// <summary>
    /// Gate session'ini temizler (kilitler). <paramref name="returnUrl"/> verilmezse Login'e
    /// yonlendirir; verilirse local URL kontrolu sonrasi oraya gider — "Ana Sayfa" butonu icin.
    /// </summary>
    [HttpGet]
    public IActionResult Logout(string? returnUrl = null)
    {
        HttpContext.Session.Remove("GateUnlockedAt");
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("Login", "Account");
    }

    // ── Dashboard (gate sonrasi — 3 sekme: Lisans, DB, Sifre Degistir) ──────

    // Iframe tab'ları eski inline HTML/JS'i cache'lemesin — her açılışta taze içerik.
    private void NoCache()
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
    }

    [HttpGet]
    [GateProtected]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        NoCache();
        var license = await _licenseService.GetCurrentAsync(ct);
        ViewBag.License             = license;
        ViewBag.MachineId           = _machineIdProvider.GetMachineId();
        ViewBag.PasswordLastChanged = await _passwordService.GetLastChangedAtAsync(ct);
        return View();
    }

    [HttpGet]
    [GateProtected]
    public async Task<IActionResult> License(CancellationToken ct)
    {
        NoCache();
        var license = await _licenseService.GetCurrentAsync(ct);
        ViewBag.License   = license;
        ViewBag.MachineId = _machineIdProvider.GetMachineId();
        return View();
    }

    [HttpGet]
    [GateProtected]
    public IActionResult UserMapping()
    {
        NoCache();
        return View();
    }

    [HttpGet]
    [GateProtected]
    public IActionResult DbSettings()
    {
        NoCache();
        return View();
    }

    [HttpGet]
    [GateProtected]
    public async Task<IActionResult> PasswordPage(CancellationToken ct)
    {
        NoCache();
        ViewBag.PasswordLastChanged = await _passwordService.GetLastChangedAtAsync(ct);
        return View();
    }

    // ── License CRUD ───────────────────────────────────────────────────────

    [HttpPost("/Gate/License/Save")]
    [GateProtected]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLicense(string licenseKey, string? securityKey, CancellationToken ct)
    {
        var result = await _licenseService.SaveAsync(licenseKey ?? string.Empty, securityKey, ct);
        return Json(new
        {
            success    = result.Success,
            message    = result.Message,
            expiryDate = result.Record.ExpiryDate?.ToString("dd.MM.yyyy"),
            concurrent = result.Record.ConcurrentLimit,
            total      = result.Record.TotalUserLimit,
        });
    }

    [HttpPost("/Gate/License/Revalidate")]
    [GateProtected]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevalidateLicense(CancellationToken ct)
    {
        var rec = await _licenseService.RevalidateAsync(ct);
        return Json(new
        {
            success    = rec.IsValid,
            message    = rec.LastError,
            expiryDate = rec.ExpiryDate?.ToString("dd.MM.yyyy"),
            concurrent = rec.ConcurrentLimit,
            total      = rec.TotalUserLimit,
        });
    }

    // ── Password Change ────────────────────────────────────────────────────

    [HttpPost("/Gate/Password/Change")]
    [GateProtected]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _passwordService.ChangeAsync(
            currentPassword ?? string.Empty,
            newPassword ?? string.Empty,
            ip,
            ct);
        return Json(new { success = result.Success, message = result.Message });
    }
}
