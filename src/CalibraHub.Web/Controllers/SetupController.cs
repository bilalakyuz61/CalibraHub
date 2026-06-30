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
[IgnoreAntiforgeryToken]
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
                new CreateDepartmentRequest(companyId, "Yonetim"),
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
            ModelState.AddModelError(string.Empty, "Islem sirasinda bir hata olustu.");
            return View(nameof(Index), model);
        }
    }

    // ── Birleşik Tanım Ekranı ─────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Definitions(
        int? companyId, int? userId, string? activeTab,
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

    private async Task<SetupUserInput> BuildSetupUserInputAsync(int? userId, CancellationToken cancellationToken)
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
            ModelState.AddModelError(string.Empty, "Islem sirasinda bir hata olustu.");
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
                    new CreateDepartmentRequest(input.CompanyId!.Value, "Yonetim"),
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
            ModelState.AddModelError(string.Empty, "Islem sirasinda bir hata olustu.");
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
        int userId, CancellationToken cancellationToken)
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

    // ── �?irket Tanımları ─────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Companies(int? id, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        var input = await BuildSetupCompanyInputAsync(id, cancellationToken);
        return View(await BuildSetupCompanyViewModelAsync(input, page, pageSize, cancellationToken));
    }

    // 2026-05-26 — Legacy "Companies" tek-sayfa form action'i.
    // Yeni "Definitions" unified screen'i kullaniliyor, bu method legacy Companies.cshtml
    // formundan POST aliyor. Ayni isimli SaveCompany action'i Definitions tarafinda da var,
    // bu yuzden burada explicit unique route + ActionName veriyoruz.
    [HttpPost("/Setup/SaveCompanyClassic")]
    [ActionName("SaveCompanyClassic")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCompanyClassic(
        SetupCompanyInput input, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        if (!ModelState.IsValid)
            return View(nameof(Companies), await BuildSetupCompanyViewModelAsync(input, page, pageSize, cancellationToken));

        try
        {
            // Son aktif şirketi pasife alma koruması
            if (!input.IsActive)
            {
                var all = await _companyDefinitionRepository.GetAllAsync(cancellationToken);
                var activeOtherCount = all.Count(c => c.IsActive && c.Id != input.Id);
                if (activeOtherCount == 0)
                {
                    ModelState.AddModelError(string.Empty, "Son aktif şirket pasife alınamaz. Sistemde en az bir aktif şirket bulunmalıdır.");
                    return View(nameof(Companies), await BuildSetupCompanyViewModelAsync(input, page, pageSize, cancellationToken));
                }
            }

            await _adminManagementService.SaveCompanyAsync(
                new SaveCompanyRequest(
                    input.Id,
                    input.Name,
                    input.Name,
                    "-", null, null, null,
                    "-", "-",
                    false, input.IsActive,
                    BuildConnectionString(input.SqlServer, input.SqlDatabase, input.SqlUsername, input.SqlPassword)),
                cancellationToken);

            TempData["SetupSuccess"] = "Sirket tanimi kaydedildi.";
            return RedirectToAction(nameof(Companies));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, "Islem sirasinda bir hata olustu.");
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

            // �?ifre gönderilmemişse mevcut bağlantı dizesinden al
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
            return Json(new { success = false, message = $"Bağlantı kurulamadı: {"Islem sirasinda bir hata olustu."}" });
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
                    new CreateDepartmentRequest(input.CompanyId!.Value, "Yonetim"),
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
            ModelState.AddModelError(string.Empty, "Islem sirasinda bir hata olustu.");
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

        // Aynı e-postaya sahip birden fazla UserProfile (farklı şirket) → gruplama.
        // Her grup = bir kişi; companies[] dizisi hangi şirketlerde aktif olduğunu taşır.
        var grouped = items
            .GroupBy(x => (x.Email ?? "").ToLowerInvariant())
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var primary = g.OrderBy(x => x.Id).First();
                var uiRole  = MapUserRoleToUiRole(primary.Role);
                return new
                {
                    id           = primary.Id,
                    fullName     = primary.FullName,
                    email        = primary.Email,
                    isActive     = g.Any(x => x.IsActive),
                    uiRole,
                    employeeCode = primary.EmployeeCode,
                    // Geriye dönük uyumluluk için tek şirket alanları (eski kod için)
                    companyId   = primary.CompanyId,
                    companyName = string.Join(", ",
                        g.Select(x => x.CompanyName).Where(n => !string.IsNullOrEmpty(n)).Distinct()),
                    // Yeni: tüm şirket bağlantıları (rol dahil)
                    companies = g.OrderBy(x => x.CompanyId).Select(x => new
                    {
                        id        = x.CompanyId,
                        name      = x.CompanyName,
                        profileId = x.Id,
                        isActive  = x.IsActive,
                        role      = MapUserRoleToUiRole(x.Role),
                    }).ToArray()
                };
            });

        return Json(grouped.ToArray());
    }

    [HttpGet]
    public async Task<IActionResult> GetCompanyUsersJson(int companyId, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return Json(new { success = false, message = "Yetkisiz erisim." });

        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var users = snapshot.Users
            .Where(u => u.CompanyId == companyId)
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                id       = u.Id,
                fullName = u.FullName,
                email    = u.Email,
                isActive = u.IsActive,
                role     = MapUserRoleToUiRole(u.Role)
            })
            .ToArray();
        return Json(users);
    }

    // Rol string'inden UI değerine ("User"/"Admin"/"SistemAdmin") dönüşüm.
    // ÖNEMLİ: DTO.Role artık Türkçe görünüm etiketi taşıyor ("Sistem Yoneticisi" vb.),
    // bu yüzden hem İngilizce enum adını hem Türkçe etiketi çözebilen TryParseRole kullanılır.
    // Eski sürüm yalnızca enum adıyla karşılaştırdığı için her zaman "User" dönüyordu.
    private static string MapUserRoleToUiRole(string? roleString)
    {
        if (!UserAuthorizationCatalog.TryParseRole(roleString, out var role))
            return "User";
        return role switch
        {
            UserRole.SystemAdmin       => "SistemAdmin",
            UserRole.DepartmentManager => "Admin",
            _                          => "User"
        };
    }

    /// <summary>
    /// "Admin"/"SistemAdmin"/"User" UI string'ini UserRole enum'a çevirir.
    /// </summary>
    private static UserRole ParseUiRole(string? uiRole) =>
        (uiRole ?? "User").Trim().ToLowerInvariant() switch
        {
            "admin"       => UserRole.DepartmentManager,
            "sistemadmin" => UserRole.SystemAdmin,
            _             => UserRole.Operator
        };

    /// <summary>
    /// �?irket bazlı rol: CompanyRoles listesinde companyId varsa onu, yoksa globalRole'ü döner.
    /// </summary>
    private static (UserRole role, IReadOnlyCollection<CalibraHub.Domain.Enums.UserPermission> perms)
        GetRoleForCompany(SetupUserInput input, int companyId, UserRole globalRole)
    {
        var entry = input.CompanyRoles?.FirstOrDefault(r => r.CompanyId == companyId);
        if (entry is not null)
        {
            var r = ParseUiRole(entry.Role);
            return (r, UserAuthorizationCatalog.GetAllowedPermissions(r));
        }
        return (globalRole, UserAuthorizationCatalog.GetAllowedPermissions(globalRole));
    }

    [HttpPost]
    public async Task<IActionResult> SaveUserJson([FromBody] SetupUserInput input, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        if (string.IsNullOrWhiteSpace(input.FullName))
            return Json(new { success = false, message = "Ad Soyad zorunludur." });
        if (string.IsNullOrWhiteSpace(input.Email))
            return Json(new { success = false, message = "E-posta zorunludur." });

        // Hangi şirket(ler)e kayıt yapılacağını belirle:
        //   companyIds (array) öncelik alır; yoksa tekil companyId'ye bakılır.
        var effectiveCompanyIds = (input.CompanyIds?.Count > 0 ? input.CompanyIds : null)
            ?? (input.CompanyId.HasValue ? [input.CompanyId.Value] : null);

        if (effectiveCompanyIds is null || effectiveCompanyIds.Count == 0)
            return Json(new { success = false, message = "En az bir şirket seçilmelidir." });

        // Yeni kayıt ise şifre zorunlu; edit ise boş bırakılırsa mevcut şifre korunur.
        // Multi-company modunda "update" = email ile eşleşen kayıt var demektir.
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var existingByEmail = snapshot.Users
            .Where(u => string.Equals(u.Email, input.Email, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var isUpdate = input.Id.HasValue || existingByEmail.Count > 0;

        if (!isUpdate && string.IsNullOrWhiteSpace(input.Password))
            return Json(new { success = false, message = "Sifre zorunludur." });

        // Global rol parse (per-company override yoksa kullanılır)
        var globalRole  = ParseUiRole(input.Role);
        var globalPerms = UserAuthorizationCatalog.GetAllowedPermissions(globalRole);

        try
        {
            // CompanyIds dizisi gönderildiyse — multi-company reconcile (şirket bazlı rol destekli)
            if (input.CompanyIds?.Count > 0)
            {
                var requestedSet = new HashSet<int>(effectiveCompanyIds);
                var existingMap  = existingByEmail.ToDictionary(u => u.CompanyId, u => u.Id);

                // 1. Mevcut + istenen = güncelle (şirket bazlı rol + extra alanlar)
                foreach (var (companyId, profileId) in existingMap.Where(kv => requestedSet.Contains(kv.Key)))
                {
                    var (compRole, _) = GetRoleForCompany(input, companyId, globalRole);
                    await _adminManagementService.UpdateUserAsync(
                        new UpdateUserRequest(
                            profileId, companyId, input.FullName, input.Email,
                            Password: string.IsNullOrWhiteSpace(input.Password) ? null : input.Password,
                            SetRole: true, Role: compRole),
                        cancellationToken);
                    await ApplyExtraFieldsAsync(input, companyId, cancellationToken);
                }

                // 2. Yeni şirketler = oluştur (şirket bazlı rol)
                foreach (var companyId in requestedSet.Except(existingMap.Keys))
                {
                    var (compRole, compPerms) = GetRoleForCompany(input, companyId, globalRole);
                    await CreateUserForCompanyAsync(companyId, input, compRole, compPerms, cancellationToken);
                }

                // 3. Listeden çıkan şirketler = pasife al
                foreach (var (companyId, profileId) in existingMap.Where(kv => !requestedSet.Contains(kv.Key)))
                {
                    var user = await _userProfileRepository.GetByIdAsync(profileId, cancellationToken);
                    if (user is not null) { user.Deactivate(); await _userProfileRepository.UpdateAsync(user, cancellationToken); }
                }

                return Json(new { success = true, message = "Kullanici kaydedildi." });
            }

            // Tek şirket — eski davranış korunur
            if (isUpdate && input.Id.HasValue)
            {
                await _adminManagementService.UpdateUserAsync(
                    new UpdateUserRequest(
                        input.Id.Value, effectiveCompanyIds[0], input.FullName, input.Email,
                        Password: string.IsNullOrWhiteSpace(input.Password) ? null : input.Password,
                        SetRole: true, Role: globalRole),
                    cancellationToken);
                return Json(new { success = true, message = "Kullanici guncellendi." });
            }

            await CreateUserForCompanyAsync(effectiveCompanyIds[0], input, globalRole, globalPerms, cancellationToken);
            return Json(new { success = true, message = "Kullanici olusturuldu." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>Belirtilen şirket için yeni UserProfile oluşturur — departman yoksa "Yonetim" departmanı açar.</summary>
    private async Task CreateUserForCompanyAsync(
        int companyId, SetupUserInput input,
        UserRole calibraRole, IReadOnlyCollection<CalibraHub.Domain.Enums.UserPermission> calibraPermissions,
        CancellationToken ct)
    {
        var allDepts = await _departmentRepository.GetAllAsync(ct);
        var dept     = allDepts.FirstOrDefault(x => x.CompanyId == companyId);
        if (dept is null)
        {
            await _adminManagementService.CreateDepartmentAsync(
                new CreateDepartmentRequest(companyId, "Yonetim"), ct);
            allDepts = await _departmentRepository.GetAllAsync(ct);
            dept     = allDepts.First(x => x.CompanyId == companyId);
        }

        var employeeCode = string.IsNullOrWhiteSpace(input.EmployeeCode)
            ? $"USR-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}"
            : input.EmployeeCode.Trim();

        await _adminManagementService.CreateUserAsync(
            new CreateUserRequest(
                companyId, input.FullName, input.Email,
                employeeCode,
                dept.Id, null,
                calibraRole, calibraPermissions,
                Password: input.Password),
            ct);

        // PhoneNumber ve IsActive CreateUserRequest'te yok — oluşturulduktan sonra doğrudan güncelle.
        await ApplyExtraFieldsAsync(input, companyId, ct);
    }

    /// <summary>
    /// PhoneNumber, EmployeeCode (güncelleme), IsActive — AdminManagementService'te olmayan alanları
    /// UserProfileRepository üzerinden doğrudan set eder.
    /// </summary>
    private async Task ApplyExtraFieldsAsync(SetupUserInput input, int companyId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.PhoneNumber) && input.IsActive) return; // nothing to patch

        var profile = await _userProfileRepository.GetByEmailAndCompanyIdAsync(
            input.Email ?? string.Empty, companyId, ct);
        if (profile is null) return;

        var phone = NormalizePhone(input.PhoneNumber);
        var rebuilt = new Domain.Entities.UserProfile
        {
            Id               = profile.Id,
            CompanyId        = profile.CompanyId,
            FullName         = profile.FullName,
            Email            = profile.Email,
            EmployeeCode     = !string.IsNullOrWhiteSpace(input.EmployeeCode) ? input.EmployeeCode.Trim() : profile.EmployeeCode,
            DepartmentId     = profile.DepartmentId,
            SupervisorUserId = profile.SupervisorUserId,
            PhoneNumber      = phone ?? profile.PhoneNumber,
            Role             = profile.Role,
            Permissions      = profile.Permissions,
        };
        rebuilt.SetPasswordHash(profile.PasswordHash);
        rebuilt.SetInterfacePreferences(profile.LanguageCode, profile.ThemeCode);
        rebuilt.SetGridPreferencesJson(profile.GridPreferencesJson);
        if (!input.IsActive) rebuilt.Deactivate();
        await _userProfileRepository.UpdateAsync(rebuilt, ct);
    }

    private static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c == '+').ToArray());
        return cleaned.Length == 0 ? null : (cleaned.Length > 30 ? cleaned[..30] : cleaned);
    }

    // ── SmartBoard Board Config ─────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> CompanyBoardConfig(CancellationToken cancellationToken)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var companies = snapshot.Companies.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var config = CalibraHub.Application.SmartBoard.SmartBoard.For(companies)
            .WithBoardKey("setup-companies")
            .WithTitle("�?irket Tanımları", subtitle: $"{companies.Count} şirket")
            .WithIcon("Building2", "indigo")
            .WithRefreshUrl("/Setup/CompanyBoardConfig")
            .WithSearchPlaceholder("�?irket adı…")
            .WithEmptyText("Henüz şirket tanımlanmamış")
            .AddHeaderAction("new", "Yeni �?irket", "Plus", "#open-new-company-modal")
            .MapEntities(c =>
            {
                // �?irket bazlı DB özelliği kaldırıldı — kartta SQL bağlantı widget'ı gösterilmez.
                var eb = CalibraHub.Application.SmartBoard.SmartBoardEntity
                    .For(c.Id.ToString(), c.Name)
                    .WithStatusBadge(c.IsActive ? "Aktif" : "Pasif", c.IsActive ? "emerald" : "slate");

                eb.WithPrimaryAction("Düzenle", "Edit", $"#edit-company-{c.Id}", "amber", hideButton: true);
                eb.WithSecondaryAction("Sil", "Trash2",
                    apiUrl: $"/Setup/DeleteCompanyPost?id={c.Id}",
                    apiMethod: "POST",
                    confirm: $"Bu şirketi silmek istediğinize emin misiniz? ({c.Name})");

                return eb;
            })
            .Build();

        return Json(config, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    [HttpGet]
    public async Task<IActionResult> UserMappingBoardConfig(CancellationToken ct)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(ct);

        var grouped = snapshot.Users
            .GroupBy(u => (u.Email ?? "").ToLowerInvariant())
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var primary = g.OrderBy(u => u.Id).First();
                var assignments = g
                    .OrderBy(u => u.CompanyId)
                    .Where(u => !string.IsNullOrEmpty(u.CompanyName))
                    .Select(u => new { u.CompanyId, u.CompanyName, Role = MapUserRoleToUiRole(u.Role) })
                    .ToList();
                return new { Primary = primary, Assignments = assignments };
            })
            .ToList();

        string[] palette = ["indigo", "violet", "blue", "emerald", "amber"];

        var config = CalibraHub.Application.SmartBoard.SmartBoard.For(grouped)
            .WithBoardKey("setup-user-mapping")
            .WithTitle("Kullanıcı-�?irket Eşleme", subtitle: $"{grouped.Count} kullanıcı")
            .WithIcon("UserCog", "violet")
            .WithRefreshUrl("/Setup/UserMappingBoardConfig")
            .WithSearchPlaceholder("Ad, e-posta…")
            .WithEmptyText("Henüz kullanıcı tanımlanmamış")
            .AddHeaderAction("new-user", "Yeni Kullanıcı", "Plus", "#open-new-user-modal")
            .MapEntities(item =>
            {
                var eb = CalibraHub.Application.SmartBoard.SmartBoardEntity
                    .For(item.Primary.Id.ToString(), item.Primary.FullName, subtitle: item.Primary.Email)
                    .WithDescription(item.Assignments.Count > 0
                        ? string.Join(" · ", item.Assignments.Select(a => $"{a.CompanyName} ({a.Role})"))
                        : "�?irket atanmamış")
                    .WithStatusBadge(item.Primary.IsActive ? "Aktif" : "Pasif",
                        item.Primary.IsActive ? "emerald" : "slate");

                for (int i = 0; i < item.Assignments.Count; i++)
                    eb.AddTextWidget($"w_co_{i}", item.Assignments[i].CompanyName ?? "�?irket",
                        item.Assignments[i].Role,
                        color: palette[i % palette.Length]);

                eb.WithPrimaryAction("Düzenle", "Edit",
                    url: $"#edit-mapping-{item.Primary.Id}",
                    color: "violet", hideButton: true);

                return eb;
            })
            .Build();

        return Json(config, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCompanyPost(int id, CancellationToken cancellationToken)
    {
        try
        {
            var all = await _companyDefinitionRepository.GetAllAsync(cancellationToken);
            if (all.Count <= 1)
                return Json(new { ok = false, error = "Son şirket silinemez." });

            await _companyDefinitionRepository.DeleteAsync(id, cancellationToken);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
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
                // �?ifre asla client'a gönderilmez — sadece var/yok bilgisi
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

            // �?ifre alanı boş gelirse mevcut bağlantı dizesini koru
            string? connectionString;
            if (string.IsNullOrWhiteSpace(input.SqlServer))
            {
                connectionString = null;
            }
            else if (string.IsNullOrWhiteSpace(input.SqlPassword) && existingConnectionString != null)
            {
                // �?ifre değiştirilmedi — mevcut bağlantı dizesini koru, sadece sunucu/db/kullanıcı güncelle
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
            return Json(new { success = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteCompanyJson(
        [FromBody] SetupDeactivateRequest? request, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return Json(new { success = false, message = "Yetkisiz erisim." });

        if (request is null || request.Id <= 0)
            return Json(new { success = false, message = "Gecersiz istek." });

        try
        {
            var all = await _companyDefinitionRepository.GetAllAsync(cancellationToken);
            if (all.Count <= 1)
                return Json(new { success = false, message = "Son şirket silinemez. Sistemde en az bir şirket bulunmalıdır." });

            await _companyDefinitionRepository.DeleteAsync(request.Id, cancellationToken);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeactivateUserJson(
        [FromBody] SetupDeactivateUserRequest? request, CancellationToken cancellationToken)
    {
        var auth = RequireAuth();
        if (auth != null) return Json(new { success = false, message = "Yetkisiz erisim." });

        if (request is null) return Json(new { success = false, message = "Gecersiz istek." });

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            // Multi-company: e-postaya göre tüm profilleri pasife al
            var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
            var profiles = snapshot.Users
                .Where(u => string.Equals(u.Email, request.Email, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var p in profiles)
            {
                var user = await _userProfileRepository.GetByIdAsync(p.Id, cancellationToken);
                if (user is not null) { user.Deactivate(); await _userProfileRepository.UpdateAsync(user, cancellationToken); }
            }
        }
        else
        {
            var user = await _userProfileRepository.GetByIdAsync(request.Id, cancellationToken);
            if (user is not null) { user.Deactivate(); await _userProfileRepository.UpdateAsync(user, cancellationToken); }
        }

        return Json(new { success = true });
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────

    // 2026-05-26: RequireAuth() devre disi — [GateProtected] TOTP zaten yetkilendirmeyi
    // saglar. Onceden hem Login hem Gate isteniyordu, iframe icinden Login'e duserdik.
    // Artik Gate session unlock'i yetiyor; SetupController + SetupUserController buradan
    // ortak rota olarak gecsin. RequireAuth() helper'i bos kalir, geri uyumluluk icin
    // var ama her zaman null doner.
    private IActionResult? RequireAuth() => null;

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

        // �?irket listesi
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
    public int Id { get; set; }
    /// <summary>E-posta ile toplu pasife alma (multi-company). Dolu ise Id göz ardı edilir.</summary>
    public string? Email { get; set; }
}
