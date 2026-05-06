using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Web.Infrastructure.Security;
using CalibraHub.Web.Models.Setup;
using CalibraHub.Web.Models.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Ilk kurulum + sirket/kullanici tanimi + baglantilar. Artik tamami Sistem Ayarlari
/// gate'i arkasinda (TOTP ile dogrulanmis oturum gerekli) — anonim acik degil.
/// </summary>
[AllowAnonymous]
[GateProtected]
public sealed class SetupController : Controller
{
    private readonly ICompanyRepository _companyDefinitionRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly IAdminManagementService _adminManagementService;
    private readonly IAdminReadService _adminReadService;
    private readonly CompanyConnectionRegistry _companyConnectionRegistry;

    public SetupController(
        ICompanyRepository companyDefinitionRepository,
        IDepartmentRepository departmentRepository,
        IUserProfileRepository userProfileRepository,
        IAdminManagementService adminManagementService,
        IAdminReadService adminReadService,
        CompanyConnectionRegistry companyConnectionRegistry)
    {
        _companyDefinitionRepository = companyDefinitionRepository;
        _departmentRepository = departmentRepository;
        _userProfileRepository = userProfileRepository;
        _adminManagementService = adminManagementService;
        _adminReadService = adminReadService;
        _companyConnectionRegistry = companyConnectionRegistry;
    }

    // ── İlk kurulum ──────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var companies = await _companyDefinitionRepository.GetAllAsync(cancellationToken);
        if (companies.Count > 0)
            return RedirectToAction(nameof(Companies));

        return View(new SetupViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Initialize(SetupViewModel model, CancellationToken cancellationToken)
    {
        var companies = await _companyDefinitionRepository.GetAllAsync(cancellationToken);
        if (companies.Count > 0)
            return RedirectToAction(nameof(Companies));

        if (!ModelState.IsValid)
            return View(nameof(Index), model);

        try
        {
            var initTaxNumber = $"TBD-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";

            var companyId = await _adminManagementService.SaveCompanyAsync(
                new SaveCompanyRequest(
                    null,
                    model.CompanyName,
                    model.CompanyName,
                    "-", null, null, null,
                    "-", initTaxNumber,
                    false, true,
                    BuildConnectionString(model.SqlServer, model.SqlDatabase, model.SqlUsername, model.SqlPassword)),
                cancellationToken);

            await _adminManagementService.CreateDepartmentAsync(
                new CreateDepartmentRequest(companyId, "YNT", "Yonetim"),
                cancellationToken);

            var allDepts = await _departmentRepository.GetAllAsync(cancellationToken);
            var dept = allDepts.First(x => x.CompanyId == companyId);

            await _adminManagementService.CreateUserAsync(
                new CreateUserRequest(
                    companyId,
                    model.AdminFullName,
                    model.AdminEmail,
                    "ADM-001",
                    dept.Id,
                    null,
                    UserRole.SystemAdmin,
                    UserAuthorizationCatalog.GetAllowedPermissions(UserRole.SystemAdmin),
                    model.AdminPassword),
                cancellationToken);

            TempData["SetupSuccess"] = "Kurulum tamamlandi. Giris yapabilirsiniz.";
            return RedirectToAction("Login", "Account");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(nameof(Index), model);
        }
    }

    // ── Birleşik Tanım Ekranı ─────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Definitions(
        int? companyId, Guid? userId, string? activeTab,
        int? companyPage, int? companyPageSize,
        int? userPage, int? userPageSize, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        var companyInput = await BuildSetupCompanyInputAsync(companyId, cancellationToken);
        var userInput    = await BuildSetupUserInputAsync(userId, cancellationToken);
        activeTab = activeTab
                    ?? TempData["SetupActiveTab"] as string
                    ?? (userId.HasValue ? "users" : companyId.HasValue ? "companies" : "companies");
        var vm = await BuildDefinitionsViewModelAsync(
            companyInput, userInput, activeTab,
            companyPage, companyPageSize, userPage, userPageSize, cancellationToken);
        return View(vm);
    }

    private async Task<SetupUserInput> BuildSetupUserInputAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (!userId.HasValue) return new SetupUserInput();
        var user = await _userProfileRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null) return new SetupUserInput();
        var parts = user.FullName.Split(' ', 2, StringSplitOptions.TrimEntries);
        return new SetupUserInput
        {
            Id        = user.Id,
            CompanyId = user.CompanyId,
            FirstName = parts.ElementAtOrDefault(0) ?? string.Empty,
            LastName  = parts.ElementAtOrDefault(1) ?? string.Empty,
            Email     = user.Email
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCompany(
        [Bind(Prefix = "CompanyInput")] SetupCompanyInput input,
        int? companyPage, int? companyPageSize,
        int? userPage, int? userPageSize, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        if (!ModelState.IsValid)
        {
            var vm = await BuildDefinitionsViewModelAsync(
                input, new SetupUserInput(), "companies",
                companyPage, companyPageSize, userPage, userPageSize, cancellationToken);
            return View(nameof(Definitions), vm);
        }

        try
        {
            string taxNumber;
            string address = "-";
            string taxOffice = "-";

            if (input.Id.HasValue)
            {
                // Mevcut şirket — değiştirilmeyen alanları koru
                var existing = await _companyDefinitionRepository.GetByIdAsync(input.Id.Value, cancellationToken);
                taxNumber = existing?.TaxNumber ?? $"TBD-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
                address   = existing?.Address   ?? "-";
                taxOffice = existing?.TaxOffice  ?? "-";
            }
            else
            {
                // Yeni şirket — benzersiz placeholder
                taxNumber = $"TBD-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
            }

            await _adminManagementService.SaveCompanyAsync(
                new SaveCompanyRequest(
                    input.Id, input.Name, input.Name, address, null, null, null,
                    taxOffice, taxNumber,
                    false, input.IsActive,
                    BuildConnectionString(input.SqlServer, input.SqlDatabase, input.SqlUsername, input.SqlPassword)),
                cancellationToken);

            TempData["SetupSuccess"] = "Sirket tanimi kaydedildi.";
            TempData["SetupActiveTab"] = "companies";
            return RedirectToAction(nameof(Definitions));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var vm = await BuildDefinitionsViewModelAsync(
                input, new SetupUserInput(), "companies",
                companyPage, companyPageSize, userPage, userPageSize, cancellationToken);
            return View(nameof(Definitions), vm);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveUserDefinition(
        [Bind(Prefix = "UserInput")] SetupUserInput input,
        int? companyPage, int? companyPageSize,
        int? userPage, int? userPageSize, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        if (!ModelState.IsValid)
        {
            var vm = await BuildDefinitionsViewModelAsync(
                new SetupCompanyInput(), input, "users",
                companyPage, companyPageSize, userPage, userPageSize, cancellationToken);
            return View(nameof(Definitions), vm);
        }

        try
        {
            var allDepts = await _departmentRepository.GetAllAsync(cancellationToken);
            var dept = allDepts.FirstOrDefault(x => x.CompanyId == input.CompanyId!.Value);
            if (dept is null)
            {
                await _adminManagementService.CreateDepartmentAsync(
                    new CreateDepartmentRequest(input.CompanyId!.Value, "YNT", "Yonetim"),
                    cancellationToken);
                allDepts = await _departmentRepository.GetAllAsync(cancellationToken);
                dept = allDepts.First(x => x.CompanyId == input.CompanyId!.Value);
            }

            await _adminManagementService.CreateUserAsync(
                new CreateUserRequest(
                    input.CompanyId!.Value, input.FullName, input.Email,
                    $"USR-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
                    dept.Id, null,
                    UserRole.SystemAdmin,
                    UserAuthorizationCatalog.GetAllowedPermissions(UserRole.SystemAdmin),
                    input.Password),
                cancellationToken);

            TempData["SetupSuccess"] = "Kullanici olusturuldu.";
            return RedirectToAction(nameof(Definitions), new { activeTab = "users" });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var vm = await BuildDefinitionsViewModelAsync(
                new SetupCompanyInput(), input, "users",
                companyPage, companyPageSize, userPage, userPageSize, cancellationToken);
            return View(nameof(Definitions), vm);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateCompany(
        int companyId, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        var company = await _companyDefinitionRepository.GetByIdAsync(companyId, cancellationToken);
        if (company != null)
        {
            company.Deactivate();
            await _companyDefinitionRepository.UpdateAsync(company, cancellationToken);
            TempData["SetupSuccess"] = "Sirket pasife alindi.";
        }

        TempData["SetupActiveTab"] = "companies";
        return RedirectToAction(nameof(Definitions));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeactivateUserDefinition(
        Guid userId, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        var user = await _userProfileRepository.GetByIdAsync(userId, cancellationToken);
        if (user != null)
        {
            user.Deactivate();
            await _userProfileRepository.UpdateAsync(user, cancellationToken);
            TempData["SetupSuccess"] = "Kullanici pasife alindi.";
        }

        return RedirectToAction(nameof(Definitions), new { activeTab = "users" });
    }

    // ── Şirket Tanımları ─────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Companies(int? id, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        var input = await BuildSetupCompanyInputAsync(id, cancellationToken);
        return View(await BuildSetupCompanyViewModelAsync(input, page, pageSize, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCompany(
        SetupCompanyInput input, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        if (!ModelState.IsValid)
            return View(nameof(Companies), await BuildSetupCompanyViewModelAsync(input, page, pageSize, cancellationToken));

        try
        {
            await _adminManagementService.SaveCompanyAsync(
                new SaveCompanyRequest(
                    input.Id,
                    input.Name,
                    input.Name,
                    "-", null, null, null,
                    "-", "-",
                    false, true,
                    BuildConnectionString(input.SqlServer, input.SqlDatabase, input.SqlUsername, input.SqlPassword)),
                cancellationToken);

            TempData["SetupSuccess"] = "Sirket tanimi kaydedildi.";
            return RedirectToAction(nameof(Companies));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(nameof(Companies), await BuildSetupCompanyViewModelAsync(input, page, pageSize, cancellationToken));
        }
    }

    [HttpPost]
    public async Task<IActionResult> TestCompanyConnection(
        [FromBody] TestCompanyConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return Json(new { success = false, message = "Yetkisiz erisim." });

        if (request is null || string.IsNullOrWhiteSpace(request.SqlServer))
            return Json(new { success = false, message = "SQL Sunucu adresi zorunludur." });

        try
        {
            string? password = request.SqlPassword;

            // Şifre gönderilmemişse mevcut bağlantı dizesinden al
            if (string.IsNullOrWhiteSpace(password) && request.Id.HasValue)
            {
                var existing = await _companyDefinitionRepository.GetByIdAsync(request.Id.Value, cancellationToken);
                if (existing?.DatabaseConnectionString != null)
                {
                    var (_, _, _, existingPwd) = ParseConnectionString(existing.DatabaseConnectionString);
                    password = existingPwd;
                }
            }

            var connectionString = BuildConnectionString(request.SqlServer, request.SqlDatabase, request.SqlUsername, password);
            if (string.IsNullOrWhiteSpace(connectionString))
                return Json(new { success = false, message = "Baglanti bilgileri eksik." });

            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString)
            {
                ConnectTimeout = 8,
                Pooling = false
            };
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return Json(new { success = true, message = "Bağlantı başarılı." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Bağlantı kurulamadı: {ex.Message}" });
        }
    }

    // ── Kullanıcı Tanımları ──────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Users(int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        return View(await BuildSetupUserViewModelAsync(new SetupUserInput(), page, pageSize, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveUser(
        SetupUserInput input, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        if (!ModelState.IsValid)
            return View(nameof(Users), await BuildSetupUserViewModelAsync(input, page, pageSize, cancellationToken));

        try
        {
            var allDepts = await _departmentRepository.GetAllAsync(cancellationToken);
            var dept = allDepts.FirstOrDefault(x => x.CompanyId == input.CompanyId!.Value);
            if (dept is null)
            {
                await _adminManagementService.CreateDepartmentAsync(
                    new CreateDepartmentRequest(input.CompanyId!.Value, "YNT", "Yonetim"),
                    cancellationToken);
                allDepts = await _departmentRepository.GetAllAsync(cancellationToken);
                dept = allDepts.First(x => x.CompanyId == input.CompanyId!.Value);
            }

            await _adminManagementService.CreateUserAsync(
                new CreateUserRequest(
                    input.CompanyId!.Value,
                    input.FullName,
                    input.Email,
                    $"USR-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
                    dept.Id,
                    null,
                    UserRole.SystemAdmin,
                    UserAuthorizationCatalog.GetAllowedPermissions(UserRole.SystemAdmin),
                    input.Password),
                cancellationToken);

            TempData["SetupSuccess"] = "Kullanici olusturuldu.";
            return RedirectToAction(nameof(Users));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(nameof(Users), await BuildSetupUserViewModelAsync(input, page, pageSize, cancellationToken));
        }
    }

    // ── Users JSON Endpoints ────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetUsersJson(string? search, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var items = snapshot.Users.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim().ToLowerInvariant();
            items = items.Where(x =>
                (x.FullName ?? "").ToLowerInvariant().Contains(q) ||
                (x.Email ?? "").ToLowerInvariant().Contains(q) ||
                (x.CompanyName ?? "").ToLowerInvariant().Contains(q));
        }
        return Json(items.Select(x => new
        {
            x.Id, x.FullName, x.Email, x.CompanyName, x.CompanyId, x.Role, x.IsActive,
            grafanaRole = x.GrafanaRole, // "Viewer"/"Designer"/"Admin"/null — frontend Düzenle akisi icin
            // CalibraHub yetkisi UI'da 3 grubun radio butonu — backend 5 enum degerini bunlara map'ler.
            //   SystemAdmin            → "SistemAdmin"
            //   DepartmentManager      → "Admin"
            //   Approver/Operator/Auditor → "User"
            uiRole = MapUserRoleToUiRole(x.Role)
        }).ToArray());
    }

    // UserRole enum stringinden UI radio button degerine donusum.
    private static string MapUserRoleToUiRole(string? roleString)
    {
        if (string.IsNullOrWhiteSpace(roleString)) return "User";
        if (string.Equals(roleString, nameof(UserRole.SystemAdmin), StringComparison.OrdinalIgnoreCase))
            return "SistemAdmin";
        if (string.Equals(roleString, nameof(UserRole.DepartmentManager), StringComparison.OrdinalIgnoreCase))
            return "Admin";
        // Approver / Operator / Auditor → User
        return "User";
    }

    [HttpPost]
    public async Task<IActionResult> SaveUserJson([FromBody] SetupUserInput input, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        if (!input.CompanyId.HasValue)
            return Json(new { success = false, message = "Sirket secimi zorunludur." });
        if (string.IsNullOrWhiteSpace(input.FullName))
            return Json(new { success = false, message = "Ad Soyad zorunludur." });
        if (string.IsNullOrWhiteSpace(input.Email))
            return Json(new { success = false, message = "E-posta zorunludur." });

        // Yeni kayit ise sifre zorunlu, edit ise bos birakilirsa mevcut sifre korunur.
        var isUpdate = input.Id.HasValue;
        if (!isUpdate && string.IsNullOrWhiteSpace(input.Password))
            return Json(new { success = false, message = "Sifre zorunludur." });

        // Grafana yetki seviyesi parse — bos/null = Grafana'ya eklenmez/cikartilir,
        // "Viewer"/"Designer"/"Admin" = ilgili rol.
        CalibraHub.Domain.Enums.GrafanaRole? grafanaRole = null;
        if (!string.IsNullOrWhiteSpace(input.GrafanaRole))
        {
            if (Enum.TryParse(input.GrafanaRole.Trim(), true, out CalibraHub.Domain.Enums.GrafanaRole parsed))
                grafanaRole = parsed;
            else
                return Json(new { success = false, message = "Grafana yetkisi gecersiz (Viewer/Designer/Admin/bos)." });
        }

        // CalibraHub rol parse — UI 3 secenek, enum 5 deger.
        // Default (Role bos / tanimsiz / yeni kayit) = "User" → Operator.
        var uiRole = string.IsNullOrWhiteSpace(input.Role) ? "User" : input.Role.Trim();
        UserRole calibraRole = uiRole.ToLowerInvariant() switch
        {
            "admin"        => UserRole.DepartmentManager,
            "sistemadmin"  => UserRole.SystemAdmin,
            "user"         => UserRole.Operator,
            _              => UserRole.Operator
        };
        var calibraPermissions = UserAuthorizationCatalog.GetAllowedPermissions(calibraRole);

        try
        {
            if (isUpdate)
            {
                await _adminManagementService.UpdateUserAsync(
                    new UpdateUserRequest(
                        input.Id!.Value,
                        input.CompanyId.Value,
                        input.FullName,
                        input.Email,
                        Password: string.IsNullOrWhiteSpace(input.Password) ? null : input.Password,
                        SetGrafanaRole: true,        // Form'dan her zaman uygulanir (bos = cikar, dolu = ekle/update)
                        GrafanaRole: grafanaRole,
                        SetRole: true,               // Form'dan rol her zaman uygulanir
                        Role: calibraRole),
                    cancellationToken);
                return Json(new { success = true, message = "Kullanici guncellendi." });
            }

            var allDepts = await _departmentRepository.GetAllAsync(cancellationToken);
            var dept = allDepts.FirstOrDefault(x => x.CompanyId == input.CompanyId.Value);
            if (dept is null)
            {
                await _adminManagementService.CreateDepartmentAsync(
                    new CreateDepartmentRequest(input.CompanyId.Value, "YNT", "Yonetim"),
                    cancellationToken);
                allDepts = await _departmentRepository.GetAllAsync(cancellationToken);
                dept = allDepts.First(x => x.CompanyId == input.CompanyId.Value);
            }

            await _adminManagementService.CreateUserAsync(
                new CreateUserRequest(
                    input.CompanyId.Value,
                    input.FullName,
                    input.Email,
                    $"USR-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
                    dept.Id,
                    null,
                    calibraRole,
                    calibraPermissions,
                    Password: input.Password,
                    GrafanaRole: grafanaRole),
                cancellationToken);

            return Json(new { success = true, message = "Kullanici olusturuldu." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ── Company JSON Endpoints ──────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetCompaniesJson(CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return Json(new { success = false, message = "Yetkisiz erisim." });

        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var result = snapshot.Companies.Select(c =>
        {
            var (server, database, username, password) = ParseConnectionString(c.DatabaseConnectionString);
            return new
            {
                c.Id, c.Name, c.IsActive,
                SqlServer   = server,
                SqlDatabase = database,
                SqlUsername = username,
                // Şifre asla client'a gönderilmez — sadece var/yok bilgisi
                HasPassword = !string.IsNullOrEmpty(password)
            };
        }).ToArray();
        return Json(result);
    }

    [HttpPost]
    public async Task<IActionResult> SaveCompanyJson(
        [FromBody] SetupCompanyInput? input, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return Json(new { success = false, message = "Yetkisiz erisim." });

        if (input is null || string.IsNullOrWhiteSpace(input.Name))
            return Json(new { success = false, message = "Sirket adi zorunludur." });

        try
        {
            string taxNumber;
            string address   = "-";
            string taxOffice = "-";
            string? existingConnectionString = null;

            if (input.Id.HasValue)
            {
                var existing = await _companyDefinitionRepository.GetByIdAsync(input.Id.Value, cancellationToken);
                taxNumber               = existing?.TaxNumber ?? $"TBD-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
                address                 = existing?.Address   ?? "-";
                taxOffice               = existing?.TaxOffice ?? "-";
                existingConnectionString = existing?.DatabaseConnectionString;
            }
            else
            {
                taxNumber = $"TBD-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}";
            }

            // Şifre alanı boş gelirse mevcut bağlantı dizesini koru
            string? connectionString;
            if (string.IsNullOrWhiteSpace(input.SqlServer))
            {
                connectionString = null;
            }
            else if (string.IsNullOrWhiteSpace(input.SqlPassword) && existingConnectionString != null)
            {
                // Şifre değiştirilmedi — mevcut bağlantı dizesini koru, sadece sunucu/db/kullanıcı güncelle
                var (_, _, _, existingPwd) = ParseConnectionString(existingConnectionString);
                connectionString = BuildConnectionString(input.SqlServer, input.SqlDatabase, input.SqlUsername, existingPwd);
            }
            else
            {
                connectionString = BuildConnectionString(input.SqlServer, input.SqlDatabase, input.SqlUsername, input.SqlPassword);
            }

            await _adminManagementService.SaveCompanyAsync(
                new SaveCompanyRequest(
                    input.Id, input.Name, input.Name, address, null, null, null,
                    taxOffice, taxNumber, false, input.IsActive,
                    connectionString),
                cancellationToken);

            return Json(new { success = true, message = "Sirket kaydedildi." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeactivateCompanyJson(
        [FromBody] SetupDeactivateRequest? request, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return Json(new { success = false, message = "Yetkisiz erisim." });

        if (request is null) return Json(new { success = false, message = "Gecersiz istek." });

        var company = await _companyDefinitionRepository.GetByIdAsync(request.Id, cancellationToken);
        if (company != null)
        {
            company.Deactivate();
            await _companyDefinitionRepository.UpdateAsync(company, cancellationToken);
        }
        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> DeactivateUserJson(
        [FromBody] SetupDeactivateUserRequest? request, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return Json(new { success = false, message = "Yetkisiz erisim." });

        if (request is null) return Json(new { success = false, message = "Gecersiz istek." });

        var user = await _userProfileRepository.GetByIdAsync(request.Id, cancellationToken);
        if (user != null)
        {
            user.Deactivate();
            await _userProfileRepository.UpdateAsync(user, cancellationToken);
        }
        return Json(new { success = true });
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────

    private IActionResult? RequireAuth()
    {
        if (User.Identity?.IsAuthenticated == true) return null;
        var returnUrl = Request.Path.Value + Request.QueryString.Value;
        return RedirectToAction("Login", "Account", new { returnUrl });
    }

    private static string? BuildConnectionString(
        string? server, string? database, string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(server)) return null;
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = server.Trim(),
            InitialCatalog = database?.Trim() ?? string.Empty,
            UserID = username?.Trim() ?? string.Empty,
            Password = password ?? string.Empty,
            TrustServerCertificate = true
        };
        return builder.ConnectionString;
    }

    private static (string Server, string Database, string Username, string Password)
        ParseConnectionString(string? connStr)
    {
        if (string.IsNullOrWhiteSpace(connStr)) return (string.Empty, string.Empty, string.Empty, string.Empty);
        try
        {
            var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connStr);
            return (b.DataSource, b.InitialCatalog, b.UserID, b.Password);
        }
        catch { return (string.Empty, string.Empty, string.Empty, string.Empty); }
    }

    private async Task<SetupDefinitionsViewModel> BuildDefinitionsViewModelAsync(
        SetupCompanyInput companyInput, SetupUserInput userInput, string activeTab,
        int? companyPage, int? companyPageSize, int? userPage, int? userPageSize,
        CancellationToken ct)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(ct);

        // Şirket listesi
        const int defaultPageSize = 10;
        var cPageSize = companyPageSize is > 0 ? companyPageSize.Value : defaultPageSize;
        var cTotal = snapshot.Companies.Count;
        var cTotalPages = cTotal == 0 ? 0 : (int)Math.Ceiling(cTotal / (double)cPageSize);
        var cPage = cTotalPages == 0 ? 1 : Math.Min(Math.Max(companyPage.GetValueOrDefault(1), 1), cTotalPages);

        // Kullanıcı listesi
        var uPageSize = userPageSize is > 0 ? userPageSize.Value : defaultPageSize;
        var uTotal = snapshot.Users.Count;
        var uTotalPages = uTotal == 0 ? 0 : (int)Math.Ceiling(uTotal / (double)uPageSize);
        var uPage = uTotalPages == 0 ? 1 : Math.Min(Math.Max(userPage.GetValueOrDefault(1), 1), uTotalPages);

        var companyOptions = snapshot.Companies
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString(), userInput.CompanyId == x.Id))
            .ToArray();

        return new SetupDefinitionsViewModel
        {
            Companies = snapshot.Companies.Skip((cPage - 1) * cPageSize).Take(cPageSize).ToArray(),
            CompanyInput = companyInput,
            CompanyListState = new GridListStateViewModel
            {
                GridKey = "def-companies", Page = cPage, PageSize = cPageSize,
                TotalCount = cTotal, TotalPages = cTotalPages, ItemLabel = "sirket",
                PageSizeOptions = [
                    new SelectListItem("10", "10", cPageSize == 10),
                    new SelectListItem("20", "20", cPageSize == 20),
                ]
            },
            Users = snapshot.Users.Skip((uPage - 1) * uPageSize).Take(uPageSize).ToArray(),
            UserInput = userInput,
            CompanyOptions = companyOptions,
            UserListState = new GridListStateViewModel
            {
                GridKey = "def-users", Page = uPage, PageSize = uPageSize,
                TotalCount = uTotal, TotalPages = uTotalPages, ItemLabel = "kullanici",
                PageSizeOptions = [
                    new SelectListItem("10", "10", uPageSize == 10),
                    new SelectListItem("20", "20", uPageSize == 20),
                ]
            },
            ActiveTab = activeTab
        };
    }

    private async Task<SetupCompanyInput> BuildSetupCompanyInputAsync(int? id, CancellationToken ct)
    {
        if (!id.HasValue) return new SetupCompanyInput();

        var snapshot = await _adminReadService.GetSnapshotAsync(ct);
        var company = snapshot.Companies.FirstOrDefault(x => x.Id == id.Value);
        if (company is null) return new SetupCompanyInput();

        var (server, database, username, password) = ParseConnectionString(company.DatabaseConnectionString);
        return new SetupCompanyInput
        {
            Id = company.Id,
            Name = company.Name,
            SqlServer = server,
            SqlDatabase = database,
            SqlUsername = username,
            SqlPassword = password,
            IsActive = company.IsActive
        };
    }

    private async Task<SetupCompanyViewModel> BuildSetupCompanyViewModelAsync(
        SetupCompanyInput input, int? page, int? pageSize, CancellationToken ct)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(ct);
        const int defaultPageSize = 20;
        var resolvedPageSize = pageSize is > 0 ? pageSize.Value : defaultPageSize;
        var totalCount = snapshot.Companies.Count;
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);
        var currentPage = totalPages == 0 ? 1 : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), totalPages);

        return new SetupCompanyViewModel
        {
            Companies = snapshot.Companies
                .Skip((currentPage - 1) * resolvedPageSize)
                .Take(resolvedPageSize)
                .ToArray(),
            Input = input,
            ListState = new GridListStateViewModel
            {
                GridKey = "setup-companies",
                Page = currentPage,
                PageSize = resolvedPageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                ItemLabel = "sirket",
                PageSizeOptions =
                [
                    new SelectListItem("10", "10", resolvedPageSize == 10),
                    new SelectListItem("20", "20", resolvedPageSize == 20),
                    new SelectListItem("50", "50", resolvedPageSize == 50),
                ]
            }
        };
    }

    private async Task<SetupUserViewModel> BuildSetupUserViewModelAsync(
        SetupUserInput input, int? page, int? pageSize, CancellationToken ct)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(ct);

        var companyOptions = snapshot.Companies
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString(), input.CompanyId == x.Id))
            .ToArray();

        const int defaultPageSize = 20;
        var resolvedPageSize = pageSize is > 0 ? pageSize.Value : defaultPageSize;
        var totalCount = snapshot.Users.Count;
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);
        var currentPage = totalPages == 0 ? 1 : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), totalPages);

        return new SetupUserViewModel
        {
            Users = snapshot.Users
                .Skip((currentPage - 1) * resolvedPageSize)
                .Take(resolvedPageSize)
                .ToArray(),
            CompanyOptions = companyOptions,
            Input = input,
            ListState = new GridListStateViewModel
            {
                GridKey = "setup-users",
                Page = currentPage,
                PageSize = resolvedPageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                ItemLabel = "kullanici",
                PageSizeOptions =
                [
                    new SelectListItem("10", "10", resolvedPageSize == 10),
                    new SelectListItem("20", "20", resolvedPageSize == 20),
                    new SelectListItem("50", "50", resolvedPageSize == 50),
                ]
            }
        };
    }
}

public sealed class TestCompanyConnectionRequest
{
    public int?    Id          { get; set; }
    public string? SqlServer   { get; set; }
    public string? SqlDatabase { get; set; }
    public string? SqlUsername { get; set; }
    public string? SqlPassword { get; set; }
}

public sealed class SetupDeactivateRequest
{
    public int Id { get; set; }
}

public sealed class SetupDeactivateUserRequest
{
    public Guid Id { get; set; }
}
