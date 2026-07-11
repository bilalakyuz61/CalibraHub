using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Models.Account;
using CalibraHub.Web.Models.Navigation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Web.Controllers;

public sealed class AccountController : Controller
{
    private const string DpapiPrefix = "dpapi:";

    private readonly ICompanyRepository _companyDefinitionRepository;
    private readonly IUiConfigurationService _uiConfigurationService;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly IUserAuthenticationService _userAuthenticationService;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IWebHostEnvironment _env;
    private readonly IPermissionService _permissionService;
    private readonly IEmailSender _emailSender;
    private readonly IPasswordHashService _passwordHashService;
    private readonly CalibraHub.Application.Services.LoginLockoutTracker _loginLockout;
    private readonly CalibraHub.Application.Auditing.IAuditTrailService _audit;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        ICompanyRepository companyDefinitionRepository,
        IUiConfigurationService uiConfigurationService,
        IUserProfileRepository userProfileRepository,
        IUserAuthenticationService userAuthenticationService,
        IDepartmentRepository departmentRepository,
        IWebHostEnvironment env,
        IPermissionService permissionService,
        IEmailSender emailSender,
        IPasswordHashService passwordHashService,
        CalibraHub.Application.Services.LoginLockoutTracker loginLockout,
        CalibraHub.Application.Auditing.IAuditTrailService audit,
        ILogger<AccountController> logger)
    {
        _companyDefinitionRepository = companyDefinitionRepository;
        _uiConfigurationService = uiConfigurationService;
        _userProfileRepository = userProfileRepository;
        _userAuthenticationService = userAuthenticationService;
        _departmentRepository = departmentRepository;
        _env = env;
        _permissionService = permissionService;
        _emailSender = emailSender;
        _passwordHashService = passwordHashService;
        _loginLockout = loginLockout;
        _audit = audit;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        var companies = await _companyDefinitionRepository.GetAllAsync(cancellationToken);
        if (companies.Count == 0)
        {
            ViewBag.ShowSetupLink = true;
        }

        var model = await BuildLoginInputModel(new LoginInputModel
        {
            ReturnUrl = returnUrl
        }, cancellationToken);

        return View(model);
    }

    [AllowAnonymous]
    [HttpGet]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> CompaniesForUser(string? email, CancellationToken cancellationToken)
    {
        var options = await GetCompanyOptionsByEmailAsync(email, null, cancellationToken);

        var payload = options
            .Select(x => new
            {
                id = x.Value,
                name = x.Text
            })
            .ToArray();

        return Json(payload);
    }

    // ── �?ifremi Unuttum ──────────────────────────────────────────────────────

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPassword(string? email = null)
    {
        return View(new ForgotPasswordInputModel { Email = email ?? string.Empty });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordInputModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(input);

        // Email ile tüm aktif kullanıcıları bul (şirket sorulmaz — şifre şirket bazlı değil)
        var allUsers = await _userProfileRepository.GetAllAsync(cancellationToken);
        var matchedUsers = allUsers
            .Where(u => u.IsActive && string.Equals(u.Email, input.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Kullanıcı bulunsun ya da bulunmasın aynı mesajı göster (enumeration önleme)
        foreach (var user in matchedUsers)
        {
            var tokenBytes = RandomNumberGenerator.GetBytes(32);
            var token = Convert.ToHexString(tokenBytes);
            var expiry = DateTime.UtcNow.AddHours(1);
            await _userProfileRepository.SetResetTokenAsync(user.Id, token, expiry, cancellationToken);

            var resetLink = Url.Action("ResetPassword", "Account", new { token }, Request.Scheme)!;

            var company = (await _companyDefinitionRepository.GetAllAsync(cancellationToken))
                .FirstOrDefault(c => c.Id == user.CompanyId);
            var companyName = company?.Name ?? "CalibraHub";

            var body = $"""
                <html><body style="font-family:system-ui,sans-serif;color:#0f172a;max-width:480px;margin:auto;padding:32px">
                <h2 style="color:#6366f1">�?ifre Sıfırlama</h2>
                <p>Merhaba <strong>{user.FullName}</strong>,</p>
                <p>�?ifrenizi sıfırlamak için aşağıdaki butona tıklayın.
                   Bu link <strong>1 saat</strong> süreyle geçerlidir.</p>
                <p style="margin:28px 0">
                  <a href="{resetLink}"
                     style="background:#6366f1;color:#fff;padding:12px 28px;border-radius:8px;text-decoration:none;font-weight:600">
                    �?ifremi Sıfırla
                  </a>
                </p>
                <p style="color:#64748b;font-size:13px">
                  Bu talebi siz yapmadıysanız bu e-postayı görmezden gelebilirsiniz.
                </p>
                <hr style="border:none;border-top:1px solid #e2e8f0;margin:24px 0" />
                <p style="color:#94a3b8;font-size:12px">{companyName} · CalibraHub</p>
                </body></html>
                """;

            await _emailSender.SendAsync(
                user.CompanyId,
                new[] { user.Email },
                "�?ifre Sıfırlama Talebi",
                body,
                attachments: null,
                cancellationToken,
                isHtml: true);
        }

        ViewBag.Sent = true;
        return View(input);
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResetPassword(string? token = null)
    {
        if (string.IsNullOrWhiteSpace(token))
            return RedirectToAction(nameof(Login));

        return View(new ResetPasswordInputModel { Token = token });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordInputModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(input);

        var (ok, strengthError) = CalibraHub.Application.Security.PasswordHasher.ValidateStrength(input.NewPassword);
        if (!ok)
        {
            ModelState.AddModelError(nameof(input.NewPassword), strengthError ?? "Şifre yeterince güçlü değil.");
            return View(input);
        }

        var user = await _userProfileRepository.GetByResetTokenAsync(input.Token, cancellationToken);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Bağlantı geçersiz veya süresi dolmuş. Lütfen yeniden talep edin.");
            return View(input);
        }

        var newHash = _passwordHashService.HashPassword(input.NewPassword);
        user.SetPasswordHash(newHash);
        await _userProfileRepository.UpdateAsync(user, cancellationToken);
        await _userProfileRepository.ClearResetTokenAsync(user.Id, cancellationToken);

        ViewBag.Success = true;
        return View(input);
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginInputModel input, CancellationToken cancellationToken)
    {
        var isAjax = string.Equals(Request.Headers["X-Requested-With"].ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        input = await BuildLoginInputModel(input, cancellationToken);

        var companyFieldHasErrors = ModelState.TryGetValue(nameof(input.CompanyId), out var companyFieldState) &&
                                    companyFieldState.Errors.Count > 0;

        if (!input.CompanyId.HasValue && !companyFieldHasErrors)
        {
            ModelState.AddModelError(nameof(input.CompanyId), "Sirket secimi zorunludur.");
        }

        if (input.CompanyId.HasValue &&
            !input.CompanyOptions.Any(x =>
                string.Equals(x.Value, input.CompanyId.Value.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(input.CompanyId), "Kullanici icin gecerli bir sirket seciniz.");
        }

        if (!ModelState.IsValid)
        {
            if (isAjax)
                return Json(new { ok = false, error = "validation" });
            return View(input);
        }

        // Brute-force koruması: hesap kilitli mi?
        var lockedUntil = _loginLockout.CheckLocked(input.Email ?? "");
        if (lockedUntil.HasValue)
        {
            var remaining = (int)Math.Ceiling((lockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            var lockMsg = $"Hesap geçici olarak kilitlendi. {remaining} dakika sonra tekrar deneyin.";
            _logger.LogWarning("[Login] Kilitli hesaba giriş denemesi: {Email} IP={Ip}",
                input.Email, HttpContext.Connection.RemoteIpAddress);
            _audit.LogEvent(CalibraHub.Application.Auditing.AuditActions.LoginFailed,
                detail: "Kilitli hesaba giriş denemesi",
                actor: LoginActor(input.CompanyId, input.Email),
                entity: "Session");
            if (isAjax)
                return Json(new { ok = false, error = "locked", message = lockMsg });
            ModelState.AddModelError(string.Empty, lockMsg);
            return View(input);
        }

        var authenticatedUser = await _userAuthenticationService.AuthenticateAsync(
            input.Email,
            input.Password,
            input.CompanyId!.Value,
            cancellationToken);

        if (authenticatedUser is null)
        {
            var nowLocked = _loginLockout.RegisterFailure(input.Email ?? "");
            var count = _loginLockout.GetCount(input.Email ?? "");
            _logger.LogWarning("[Login] Başarısız giriş denemesi: {Email} IP={Ip} Deneme={Count}{Locked}",
                input.Email,
                HttpContext.Connection.RemoteIpAddress,
                count,
                nowLocked ? " → HESAP KİLİTLENDİ" : "");
            _audit.LogEvent(CalibraHub.Application.Auditing.AuditActions.LoginFailed,
                detail: nowLocked ? $"Başarısız giriş (deneme {count}) — hesap kilitlendi" : $"Başarısız giriş (deneme {count})",
                actor: LoginActor(input.CompanyId, input.Email),
                entity: "Session");

            if (nowLocked)
            {
                var lockMsg = $"Çok fazla başarısız deneme. Hesap {CalibraHub.Application.Services.LoginLockoutTracker.LockoutMinutes} dakika kilitlendi.";
                if (isAjax)
                    return Json(new { ok = false, error = "locked", message = lockMsg });
                ModelState.AddModelError(string.Empty, lockMsg);
                return View(input);
            }

            var remaining = CalibraHub.Application.Services.LoginLockoutTracker.MaxAttempts - count;
            var credMsg = remaining > 0
                ? $"Şirket, e-posta veya şifre hatalı. ({remaining} deneme hakkı kaldı)"
                : "Şirket, e-posta veya şifre hatalı.";
            if (isAjax)
                return Json(new { ok = false, error = "credentials", message = credMsg, remaining });
            ModelState.AddModelError(string.Empty, credMsg);
            return View(input);
        }

        // Başarılı giriş — sayacı sıfırla
        _loginLockout.Reset(input.Email ?? "");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, authenticatedUser.Id.ToString()),
            new(ClaimTypes.Name, authenticatedUser.FullName),
            new(ClaimTypes.Email, authenticatedUser.Email),
            new(ClaimTypes.Role, authenticatedUser.Role),
            new("company_id", authenticatedUser.CompanyId.ToString()),
            new("company_name", authenticatedUser.CompanyName),
        };
        // 2026-06-06: department_id claim — RequirePermissionAttribute departmana göre izin
        // çözümlemesinde kullanır. NULL ise eklenmez.
        if (authenticatedUser.DepartmentId.HasValue)
            claims.Add(new Claim("department_id", authenticatedUser.DepartmentId.Value.ToString()));

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = input.RememberMe,
            ExpiresUtc = DateTimeOffset.Now.AddHours(input.RememberMe ? 24 : 8)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            authProperties);

        _audit.LogEvent(CalibraHub.Application.Auditing.AuditActions.Login,
            detail: input.RememberMe ? "Giriş (beni hatırla)" : "Giriş",
            actor: new CalibraHub.Application.Auditing.AuditActor(
                authenticatedUser.CompanyId, authenticatedUser.Id, authenticatedUser.Email,
                HttpContext.Connection.RemoteIpAddress?.ToString(), "Web"),
            entity: "Session");

        // Login sayfasındaki tema toggle'ı localStorage'a kaydeder.
        // Form submit sırasında hidden input aracılığıyla sunucuya taşınır ve kullanıcı tercihine işlenir.
        if (!string.IsNullOrWhiteSpace(input.ThemeCode) &&
            (string.Equals(input.ThemeCode, "dark",  StringComparison.OrdinalIgnoreCase) ||
             string.Equals(input.ThemeCode, "light", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var currentPref = await _uiConfigurationService.GetUserPreferenceAsync(authenticatedUser.Id, cancellationToken);
                await _uiConfigurationService.SaveUserPreferenceAsync(
                    new SaveUserInterfacePreferenceRequest(
                        authenticatedUser.Id,
                        currentPref?.LanguageCode ?? "tr-TR",
                        input.ThemeCode.ToLowerInvariant()),
                    cancellationToken);
            }
            catch { /* tema kaydı başarısız olsa da login devam eder */ }
        }

        var redirectUrl = (!string.IsNullOrWhiteSpace(input.ReturnUrl) && Url.IsLocalUrl(input.ReturnUrl))
            ? input.ReturnUrl
            : Url.Action("Index", "Home")!;

        if (isAjax)
            return Json(new { ok = true, redirect = redirectUrl });

        return Redirect(redirectUrl);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveInterfacePreferences(
        string languageCode,
        string themeCode,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        try
        {
            await _uiConfigurationService.SaveUserPreferenceAsync(
                new SaveUserInterfacePreferenceRequest(userId, languageCode, themeCode),
                cancellationToken);

            TempData["AdminSuccess"] = "Dil ve tema tercihleriniz kaydedildi.";
        }
        catch (ArgumentException ex)
        {
            TempData["AdminError"] = ex.Message;
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword()
    {
        return View(new ChangePasswordInputModel());
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordInputModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        try
        {
            await _userAuthenticationService.ChangePasswordAsync(
                userId,
                input.CurrentPassword,
                input.NewPassword,
                cancellationToken);

            TempData["PasswordSuccess"] = "Sifre basariyla degistirildi.";
            return RedirectToAction(nameof(ChangePassword));
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(input);
        }
    }

    // ── Profile (kullanici kendi bilgileri) ────────────────────────────────
    // Email + Role + IsActive DEGISTIRILEMEZ — security (self-elevation,
    // identity hijack). Bunlar admin tarafindan yonetilir. Diger butun alanlar
    // (ad, telefon, departman, amir, dil, tema, personel kodu) duzenlenebilir.
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        var user = await _userProfileRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        var model = await BuildProfileModelAsync(user, cancellationToken);
        // Mevcut degerleri form'a doldur
        model.FullName         = user.FullName;
        model.EmployeeCode     = user.EmployeeCode;
        model.DepartmentId     = user.DepartmentId;
        model.SupervisorUserId = user.SupervisorUserId;
        model.PhoneNumber      = user.PhoneNumber;
        model.LanguageCode     = user.LanguageCode;
        model.ThemeCode        = user.ThemeCode;

        return View(model);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileInputModel input, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        var existing = await _userProfileRepository.GetByIdAsync(userId, cancellationToken);
        if (existing is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        if (!ModelState.IsValid)
        {
            // Dropdownlari tekrar doldur
            var redrawn = await BuildProfileModelAsync(existing, cancellationToken);
            input.Email       = redrawn.Email;
            input.RoleLabel   = redrawn.RoleLabel;
            input.CompanyName = redrawn.CompanyName;
            input.Departments = redrawn.Departments;
            input.Supervisors = redrawn.Supervisors;
            return View(input);
        }

        // Cycle koruma: kullanici kendisini amir secemez
        var supervisorId = input.SupervisorUserId == existing.Id ? null : input.SupervisorUserId;

        // Telefon normalize — bosluk/tire/parantez temizle, sadece rakam + "+"
        var cleanPhone = NormalizePhone(input.PhoneNumber);

        var rebuilt = new CalibraHub.Domain.Entities.UserProfile
        {
            Id               = existing.Id,
            CompanyId        = existing.CompanyId,
            FullName         = (input.FullName ?? string.Empty).Trim(),
            Email            = existing.Email,                       // KORUNUR
            EmployeeCode     = string.IsNullOrWhiteSpace(input.EmployeeCode) ? existing.EmployeeCode : input.EmployeeCode.Trim(),
            DepartmentId     = input.DepartmentId,
            SupervisorUserId = supervisorId,
            PhoneNumber      = cleanPhone,
            Role             = existing.Role,                        // KORUNUR
            Permissions      = existing.Permissions,
        };
        rebuilt.SetPasswordHash(existing.PasswordHash);
        rebuilt.SetInterfacePreferences(input.LanguageCode ?? "tr-TR", input.ThemeCode ?? "light");
        rebuilt.SetGridPreferencesJson(existing.GridPreferencesJson);
        if (!existing.IsActive) rebuilt.Deactivate();

        await _userProfileRepository.UpdateAsync(rebuilt, cancellationToken);

        TempData["ProfileSuccess"] = "Profil bilgileriniz güncellendi.";
        return RedirectToAction(nameof(Profile));
    }

    private async Task<ProfileInputModel> BuildProfileModelAsync(
        CalibraHub.Domain.Entities.UserProfile user,
        CancellationToken ct)
    {
        // Sirket adi
        var companies = await _companyDefinitionRepository.GetAllAsync(ct);
        var company = companies.FirstOrDefault(c => c.Id == user.CompanyId);

        // Departmanlar — sirkete ait + aktif
        var depts = (await _departmentRepository.GetAllAsync(ct))
            .Where(d => d.CompanyId == user.CompanyId && d.IsActive)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new SelectListItem
            {
                Value = d.Id.ToString(),
                Text  = d.Name,
                Selected = d.Id == user.DepartmentId,
            }).ToList();
        depts.Insert(0, new SelectListItem { Value = "", Text = "(Departman yok)" });

        // Amir adaylari — sirkete ait, aktif, SystemAdmin haric, kendisi haric
        var sups = (await _userProfileRepository.GetAllAsync(ct))
            .Where(u => u.CompanyId == user.CompanyId
                        && u.IsActive
                        && u.Role != UserRole.SystemAdmin
                        && u.Id != user.Id)
            .OrderBy(u => u.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(u => new SelectListItem
            {
                Value = u.Id.ToString(),
                Text  = u.FullName,
                Selected = u.Id == user.SupervisorUserId,
            }).ToList();
        sups.Insert(0, new SelectListItem { Value = "", Text = "(Amir seçilmedi)" });

        return new ProfileInputModel
        {
            Email       = user.Email,
            CompanyName = company?.Name ?? string.Empty,
            RoleLabel   = GetRoleLabel(user.Role),
            Departments = depts,
            Supervisors = sups,
        };
    }

    private static string GetRoleLabel(UserRole role) => role switch
    {
        UserRole.SystemAdmin       => "Sistem Yöneticisi",
        UserRole.DepartmentManager => "Departman Yöneticisi",
        UserRole.Approver          => "Onaylayıcı",
        UserRole.Operator          => "Operatör",
        UserRole.Auditor           => "Denetçi",
        _ => role.ToString(),
    };

    private static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (cleaned.Length == 0) return null;
        if (cleaned.Length > 30) cleaned = cleaned[..30];
        return cleaned;
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        _audit.LogEvent(CalibraHub.Application.Auditing.AuditActions.Logout, entity: "Session");
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public async Task<IActionResult> Logout(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true)
            _audit.LogEvent(CalibraHub.Application.Auditing.AuditActions.Logout, entity: "Session");
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    /// <summary>Login öncesi audit olayları için aktör — kullanıcı henüz authenticate değil.</summary>
    private CalibraHub.Application.Auditing.AuditActor LoginActor(int? companyId, string? email) =>
        new(companyId, null, email, HttpContext.Connection.RemoteIpAddress?.ToString(), "Web");

    // ── Oturum idle-timeout ────────────────────────────────────────────────
    // Client (Shell) idle-timer'ı bu politikayı okuyup geri sayımlı uyarı + logout uygular.
    // Sunucu backstop'u ayrıdır (auth cookie ExpireTimeSpan = appsettings Authentication:IdleMinutes).

    /// <summary>Per-company oturum atalet süresi (dk). 0 = kapalı. warnSeconds = kapanmadan önce uyarı süresi (sn).</summary>
    [Authorize]
    [HttpGet("/Account/SessionPolicy")]
    public async Task<IActionResult> SessionPolicy(
        [FromServices] CalibraHub.Application.Abstractions.Services.ICompanyParameterService companyParameters,
        CancellationToken ct)
    {
        int idle;
        try
        {
            idle = await companyParameters.GetIntAsync(
                       CalibraHub.Application.Constants.SecurityParameters.FormCode,
                       CalibraHub.Application.Constants.SecurityParameters.SessionIdleMinutesKey, ct)
                   ?? CalibraHub.Application.Constants.SecurityParameters.DefaultSessionIdleMinutes;
        }
        catch { idle = CalibraHub.Application.Constants.SecurityParameters.DefaultSessionIdleMinutes; }
        if (idle < 0) idle = 0;
        return Json(new { idleMinutes = idle, warnSeconds = CalibraHub.Application.Constants.SecurityParameters.WarningSeconds });
    }

    /// <summary>
    /// Keepalive — aktif kullanıcının Shell'i throttle'lı çağırır; [Authorize] isteği sliding
    /// auth cookie'sini tazeler (ExpireTimeSpan yenilenir), böylece aktif ama sunucuya istek
    /// atmayan (okuyan) kullanıcı backstop'a takılmaz. Gövdesiz; 204 döner. State değiştirmez
    /// (yalnız cookie renewal) → CSRF açısından zararsız, IgnoreAntiforgeryToken.
    /// </summary>
    [Authorize]
    [HttpPost("/Account/KeepAlive")]
    [IgnoreAntiforgeryToken]
    public IActionResult KeepAlive() => NoContent();

    // ── Veritabanı bağlantı ayarları (kurulum sihirbazı + sistem yönetimi) ──────────────────
    // Kurulum tamamlanmamışsa (hiç şirket yok) → anonim erişime açık.
    // Kurulum tamamsa → yalnızca giriş yapılmış kullanıcı erişebilir.

    private async Task<bool> IsSetupCompleteAsync()
    {
        try
        {
            var companies = await _companyDefinitionRepository.GetAllAsync(HttpContext.RequestAborted);
            return companies.Count > 0;
        }
        catch
        {
            // DB henüz erişilebilir değilse kurulum tamamlanmamış say → anonim geç
            return false;
        }
    }

    [AllowAnonymous]
    [HttpGet("/Account/GetDbSettings")]
    public async Task<IActionResult> GetDbSettings()
    {
        if (await IsSetupCompleteAsync() && !User.Identity!.IsAuthenticated)
            return Json(new { success = false, message = "Yetkisiz erişim." });

        var appSettingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
        try
        {
            var json = System.IO.File.ReadAllText(appSettingsPath);
            var node = JsonNode.Parse(json);
            var raw = node?["CalibraDatabase"]?["ConnectionString"]?.GetValue<string>() ?? string.Empty;
            var plain = raw.StartsWith(DpapiPrefix, StringComparison.Ordinal)
                ? DecryptDpapi(raw)
                : raw;
            // Bağlantı dizesini bileşenlerine ayır
            string server = string.Empty, database = string.Empty, user = string.Empty;
            if (!string.IsNullOrWhiteSpace(plain))
            {
                try
                {
                    var sb = new SqlConnectionStringBuilder(plain);
                    server = sb.DataSource;
                    database = sb.InitialCatalog;
                    user = sb.UserID;
                }
                catch { /* parse edilemezse boş bırak */ }
            }

            return Json(new { success = true, server, database, user });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Islem sirasinda bir hata olustu.", server = string.Empty, database = string.Empty, user = string.Empty });
        }
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("/Account/TestDbSettings")]
    public async Task<IActionResult> TestDbSettings([FromBody] DbSettingsInput? input)
    {
        if (await IsSetupCompleteAsync() && !User.Identity!.IsAuthenticated)
            return Json(new { success = false, message = "Yetkisiz erişim." });

        try
        {
            if (input is null)
                return Json(new { success = false, message = "Gecersiz istek." });

            if (string.IsNullOrWhiteSpace(input.Server))
                return Json(new { success = false, message = "Sunucu adi zorunludur." });

            if (string.IsNullOrWhiteSpace(input.Database))
                return Json(new { success = false, message = "Veritabani adi zorunludur." });

            // Mevcut ayarları baz al, sadece değiştirilen alanları üzerine yaz
            var connStr = MergeWithSaved(input);
            var builder = new SqlConnectionStringBuilder(connStr) { ConnectTimeout = 3 };
            await using var conn = new SqlConnection(builder.ConnectionString);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await conn.OpenAsync(cts.Token);

            return Json(new { success = true, message = "Baglanti basarili." });
        }
        catch (OperationCanceledException)
        {
            return Json(new { success = false, message = "Baglanti zaman asimina ugradi (3 sn)." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("/Account/SaveDbSettings")]
    public async Task<IActionResult> SaveDbSettings([FromBody] DbSettingsInput? input)
    {
        if (await IsSetupCompleteAsync() && !User.Identity!.IsAuthenticated)
            return Json(new { success = false, message = "Yetkisiz erişim." });

        try
        {
            if (input is null)
                return Json(new { success = false, message = "Gecersiz istek." });

            if (string.IsNullOrWhiteSpace(input.Server))
                return Json(new { success = false, message = "Sunucu adi zorunludur." });

            if (string.IsNullOrWhiteSpace(input.Database))
                return Json(new { success = false, message = "Veritabani adi zorunludur." });

            // Mevcut ayarları baz al, sadece değiştirilen alanları üzerine yaz
            var connStr = MergeWithSaved(input);

            // Kaydetmeden önce bağlantıyı test et
            try
            {
                var testBuilder = new SqlConnectionStringBuilder(connStr) { ConnectTimeout = 3 };
                await using var testConn = new SqlConnection(testBuilder.ConnectionString);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                await testConn.OpenAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return Json(new { success = false, message = "Baglanti testi basarisiz: zaman asimina ugradi (3 sn). Ayarlar kaydedilmedi." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Baglanti testi basarisiz: " + "Islem sirasinda bir hata olustu." + " Ayarlar kaydedilmedi." });
            }

            // Test başarılı — kaydet
            var appSettingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
            var json = await System.IO.File.ReadAllTextAsync(appSettingsPath, Encoding.UTF8);
            var node = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidOperationException("appsettings.json okunamadi.");

            if (node["CalibraDatabase"] is not JsonObject dbNode)
            {
                dbNode = new JsonObject();
                node["CalibraDatabase"] = dbNode;
            }

            var encrypted = await Task.Run(() => EncryptDpapi(connStr));
            dbNode["ConnectionString"] = encrypted;

            var opts = new JsonSerializerOptions { WriteIndented = true };
            await System.IO.File.WriteAllTextAsync(appSettingsPath, node.ToJsonString(opts), Encoding.UTF8);

            return Json(new { success = true, message = "Baglanti dogrulandi ve ayarlar kaydedildi. Uygulamayı yeniden başlatın." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Kayit hatasi: " + "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>
    /// Kaydedilmiş bağlantı dizesini okur, input'ta dolu olan alanları üzerine yazar.
    /// �?ifre alanı boş bırakıldığında mevcut şifre korunur.
    /// </summary>
    private string MergeWithSaved(DbSettingsInput input)
    {
        // Mevcut bağlantı dizesini oku
        var saved = new SqlConnectionStringBuilder();
        try
        {
            var appSettingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
            var json = System.IO.File.ReadAllText(appSettingsPath);
            var node = JsonNode.Parse(json);
            var raw = node?["CalibraDatabase"]?["ConnectionString"]?.GetValue<string>() ?? string.Empty;
            var plain = raw.StartsWith(DpapiPrefix, StringComparison.Ordinal) ? DecryptDpapi(raw) : raw;
            if (!string.IsNullOrWhiteSpace(plain))
                saved = new SqlConnectionStringBuilder(plain);
        }
        catch { /* mevcut ayar okunamazsa boş başla */ }

        // Input'ta dolu olan alanları uygula
        if (!string.IsNullOrWhiteSpace(input.Server))   saved.DataSource     = input.Server.Trim();
        if (!string.IsNullOrWhiteSpace(input.Database)) saved.InitialCatalog = input.Database.Trim();
        if (!string.IsNullOrWhiteSpace(input.User))     saved.UserID         = input.User.Trim();
        if (!string.IsNullOrEmpty(input.Password))      saved.Password       = input.Password;

        saved.TrustServerCertificate = true;
        saved.ConnectTimeout = 30;

        return saved.ConnectionString;
    }

    private static string EncryptDpapi(string plainText)
    {
        if (!OperatingSystem.IsWindows()) return plainText;
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine);
        return DpapiPrefix + Convert.ToBase64String(encrypted);
    }

    private static string DecryptDpapi(string value)
    {
        if (!OperatingSystem.IsWindows()) return value;
        if (!value.StartsWith(DpapiPrefix, StringComparison.Ordinal)) return value;
        try
        {
            var bytes = Convert.FromBase64String(value[DpapiPrefix.Length..]);
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch { return string.Empty; }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task<LoginInputModel> BuildLoginInputModel(
        LoginInputModel model,
        CancellationToken cancellationToken)
    {
        var companyOptions = await GetCompanyOptionsByEmailAsync(
            model.Email,
            model.CompanyId,
            cancellationToken);

        var selectedCompanyId = model.CompanyId.HasValue &&
                                companyOptions.Any(x =>
                                    string.Equals(
                                        x.Value,
                                        model.CompanyId.Value.ToString(),
                                        StringComparison.OrdinalIgnoreCase))
            ? model.CompanyId
            : null;

        return new LoginInputModel
        {
            CompanyId = selectedCompanyId,
            Email = model.Email,
            Password = model.Password,
            RememberMe = model.RememberMe,
            ReturnUrl = model.ReturnUrl,
            CompanyOptions = companyOptions
        };
    }

    public sealed class DbSettingsInput
    {
        public string? Server { get; set; }
        public string? Database { get; set; }
        public string? User { get; set; }
        public string? Password { get; set; }
    }

    private async Task<IReadOnlyCollection<SelectListItem>> GetCompanyOptionsByEmailAsync(
        string? email,
        int? selectedCompanyId,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return Array.Empty<SelectListItem>();
        }

        var users = await _userProfileRepository.GetAllAsync(cancellationToken);
        var companyIds = users
            .Where(x =>
                x.IsActive &&
                x.CompanyId != 0 &&
                string.Equals(x.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.CompanyId)
            .Distinct()
            .ToHashSet();

        if (companyIds.Count == 0)
        {
            return Array.Empty<SelectListItem>();
        }

        var companies = await _companyDefinitionRepository.GetAllAsync(cancellationToken);
        return companies
            .Where(x => x.IsActive && companyIds.Contains(x.Id))
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(
                x.Name,
                x.Id.ToString(),
                selectedCompanyId == x.Id))
            .ToArray();
    }

    /// <summary>
    /// Shell menüsünü anlık yetkilerle döndürür.
    /// Shell.jsx focus/visibility değişiminde bu endpoint'i poll ederek
    /// yetki değişikliklerini sayfa yenilemesi olmadan yansıtır.
    /// </summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetMenuItems(CancellationToken cancellationToken)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdStr, out var userId) || userId <= 0)
            return Json(new { menu = Array.Empty<object>() });

        var roleStr = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var role = UserAuthorizationCatalog.TryParseRole(roleStr, out var r)
            ? r : UserRole.Operator;
        var isSystemAdmin = role == UserRole.SystemAdmin;

        var languageCode = User.FindFirstValue("language_code") ?? "tr-TR";
        var full = MenuDefinition.GetMainMenu(isSystemAdmin, languageCode);
        var deptStr = User.FindFirstValue("department_id");
        int? deptId = int.TryParse(deptStr, out var d) && d > 0 ? d : null;

        var filtered = await MenuDefinition.FilterByPermissionAsync(
            full, _permissionService, userId, role, deptId, cancellationToken);

        return Json(new { menu = filtered }, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
    }
}
