using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Web.Controllers;

public sealed class AccountController : Controller
{
    private const string DpapiPrefix = "dpapi:";

    private readonly ICompanyDefinitionRepository _companyDefinitionRepository;
    private readonly IUiConfigurationService _uiConfigurationService;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly IUserAuthenticationService _userAuthenticationService;
    private readonly IWebHostEnvironment _env;

    public AccountController(
        ICompanyDefinitionRepository companyDefinitionRepository,
        IUiConfigurationService uiConfigurationService,
        IUserProfileRepository userProfileRepository,
        IUserAuthenticationService userAuthenticationService,
        IWebHostEnvironment env)
    {
        _companyDefinitionRepository = companyDefinitionRepository;
        _uiConfigurationService = uiConfigurationService;
        _userProfileRepository = userProfileRepository;
        _userAuthenticationService = userAuthenticationService;
        _env = env;
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

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginInputModel input, CancellationToken cancellationToken)
    {
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
            return View(input);
        }

        var authenticatedUser = await _userAuthenticationService.AuthenticateAsync(
            input.Email,
            input.Password,
            input.CompanyId!.Value,
            cancellationToken);

        if (authenticatedUser is null)
        {
            ModelState.AddModelError(string.Empty, "Sirket, e-posta veya sifre hatali.");
            return View(input);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, authenticatedUser.Id.ToString()),
            new(ClaimTypes.Name, authenticatedUser.FullName),
            new(ClaimTypes.Email, authenticatedUser.Email),
            new(ClaimTypes.Role, authenticatedUser.Role),
            new("company_id", authenticatedUser.CompanyId.ToString()),
            new("company_name", authenticatedUser.CompanyName)
        };

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

        if (!string.IsNullOrWhiteSpace(input.ReturnUrl) && Url.IsLocalUrl(input.ReturnUrl))
        {
            return Redirect(input.ReturnUrl);
        }

        return RedirectToAction("Index", "Home");
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
        if (!Guid.TryParse(userIdClaim, out var userId))
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
        if (!Guid.TryParse(userIdClaim, out var userId))
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

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public async Task<IActionResult> Logout(string? returnUrl)
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    // ── Veritabanı bağlantı ayarları (login ekranı, anonim) ──────────────────

    [AllowAnonymous]
    [HttpGet("/Account/GetDbSettings")]
    public IActionResult GetDbSettings()
    {
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
            return Json(new { success = false, message = ex.Message, server = string.Empty, database = string.Empty, user = string.Empty });
        }
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("/Account/TestDbSettings")]
    public async Task<IActionResult> TestDbSettings([FromBody] DbSettingsInput? input)
    {
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
            return Json(new { success = false, message = ex.Message });
        }
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("/Account/SaveDbSettings")]
    public async Task<IActionResult> SaveDbSettings([FromBody] DbSettingsInput? input)
    {
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
                return Json(new { success = false, message = "Baglanti testi basarisiz: " + ex.Message + " Ayarlar kaydedilmedi." });
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
            return Json(new { success = false, message = "Kayit hatasi: " + ex.Message });
        }
    }

    /// <summary>
    /// Kaydedilmiş bağlantı dizesini okur, input'ta dolu olan alanları üzerine yazar.
    /// Şifre alanı boş bırakıldığında mevcut şifre korunur.
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
}
