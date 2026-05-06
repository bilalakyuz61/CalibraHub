using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Web.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    public GateController(
        IGatePasswordService passwordService,
        ILicenseService licenseService,
        IMachineIdProvider machineIdProvider)
    {
        _passwordService   = passwordService;
        _licenseService    = licenseService;
        _machineIdProvider = machineIdProvider;
    }

    // ── Gate Login ──────────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult Index(string? returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(string password, string? returnUrl, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(password) || !await _passwordService.VerifyAsync(password, ct))
        {
            // Brute-force koruma: yanlis girisler 1 saniye geciktirilir
            await Task.Delay(1000, ct);
            TempData["GateError"] = "Gecersiz sifre.";
            ViewBag.ReturnUrl = returnUrl;
            return View(nameof(Index));
        }

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

    [HttpGet]
    [GateProtected]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var license = await _licenseService.GetCurrentAsync(ct);
        ViewBag.License             = license;
        ViewBag.MachineId           = _machineIdProvider.GetMachineId();
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
