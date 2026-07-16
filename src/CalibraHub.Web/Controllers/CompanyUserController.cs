using CalibraHub.Application.Constants;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// CompanyUserController — Kullanici Tanimlamalari (rol-duyarli tek ekran).
///
/// Normal kullanici: yalnizca kendi sirketinin kullanicilari — CompanyId claims'den alinir.
/// SystemAdmin: tum sirketlerdeki kullanicilari gorur/yonetir — CompanyId form'dan gelir.
///
/// SetupUserController bu ekrana yonlendirilmis durumdadir; artik ayri ekran yok.
/// </summary>
[Authorize]
// "�?irket ve Kullanıcı Tanımlamaları" formu — admin kullanıcı CRUD ekranı.
// Kullanıcı Tanımlamaları — admin (DepartmentManager) erişir. SystemAdmin rol ataması/düzenlemesi
// server-side korumalı (bkz. Save + GetForm IsSystemAdmin guard'ları). SetupDefinitions'tan taşındı (2026-07-11).
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.UserManagement)]
public sealed class CompanyUserController : Controller
{
    private const string DefaultPassword = "12345678";

    private readonly IUserProfileRepository _userRepo;
    private readonly IAdminManagementService _adminService;
    private readonly IDepartmentRepository _deptRepo;
    private readonly ICompanyRepository _companyRepo;
    private readonly IPasswordHashService _passwordHashService;
    private readonly IAuditTrailService _audit;

    public CompanyUserController(
        IUserProfileRepository userRepo,
        IAdminManagementService adminService,
        IDepartmentRepository deptRepo,
        ICompanyRepository companyRepo,
        IPasswordHashService passwordHashService,
        IAuditTrailService audit)
    {
        _userRepo = userRepo;
        _adminService = adminService;
        _deptRepo = deptRepo;
        _companyRepo = companyRepo;
        _passwordHashService = passwordHashService;
        _audit = audit;
    }

    private (int CompanyId, int UserId) GetCurrentUser()
    {
        var companyIdStr = User.FindFirstValue("company_id") ?? string.Empty;
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        int.TryParse(companyIdStr, out var companyId);
        int.TryParse(userIdStr, out var userId);
        return (companyId, userId);
    }

    private int GetCurrentCompanyId() => GetCurrentUser().CompanyId;

    /// <summary>
    /// Mevcut kullanicinin SystemAdmin oldugunu dogrular.
    /// SystemAdmin modunda CompanyId client'tan kabul edilir (tum sirketleri yonetebilir).
    /// </summary>
    private bool IsSystemAdmin()
    {
        var roleString = User.FindFirstValue(ClaimTypes.Role);
        return UserAuthorizationCatalog.TryParseRole(roleString, out var role) &&
               role == UserRole.SystemAdmin;
    }

    // ── Liste sayfasi ─────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";

        var config = await BuildCompanyUsersBoardConfigAsync(ct);
        ViewData["Title"] = "Kullanıcı Tanımlamaları";
        ViewData["BoardConfigJson"] = JsonSerializer.Serialize(config,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> BoardConfig(CancellationToken ct)
    {
        var config = await BuildCompanyUsersBoardConfigAsync(ct);
        return Json(config, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    private async Task<object> BuildCompanyUsersBoardConfigAsync(CancellationToken ct)
    {
        var companyId = GetCurrentCompanyId();
        var allUsers = await _userRepo.GetAllAsync(ct);

        // 2026-06-06: Silinen (Deactivate edilmiş) kullanıcılar listede gözükmesin.
        // Backend Delete endpoint soft-delete yapıyor (IsActive=false); kullanıcı
        // tarafında "silindi → kayboldu" beklentisini karşılamak için filtre.
        var visibleUsers = allUsers
            .Where(u => u.CompanyId == companyId && u.IsActive)
            .OrderBy(u => u.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var supervisorLookup = visibleUsers.ToDictionary(u => u.Id, u => u.FullName);

        var departments = (await _deptRepo.GetAllAsync(ct))
            .ToDictionary(d => d.Id, d => d.Name);

        return CalibraHub.Application.SmartBoard.SmartBoard.For(visibleUsers)
            .WithBoardKey("company-users")
            .WithTitle("Kullanıcı Tanımlamaları", subtitle: $"{visibleUsers.Count} kullanıcı")
            .WithIcon("Users", "indigo")
            .WithRefreshUrl("/CompanyUser/BoardConfig")
            .WithSearchPlaceholder("Ad, e-posta…")
            .WithEmptyText("Henüz kullanıcı tanımlanmamış")
            .AddHeaderAction("new", "Yeni Kullanıcı", "Plus", "#open-new-user-modal")
            .MapEntities(u =>
            {
                var roleLabel = GetRoleLabel(u.Role);
                var deptName = u.DepartmentId.HasValue && departments.TryGetValue(u.DepartmentId.Value, out var dn) ? dn : null;

                var eb = CalibraHub.Application.SmartBoard.SmartBoardEntity
                    .For(u.Id.ToString(), u.FullName, subtitle: u.Email)
                    .WithDescription(roleLabel)
                    .WithStatusBadge(u.IsActive ? "Aktif" : "Pasif", u.IsActive ? "emerald" : "slate");

                eb.AddTextWidget("w_role", "Rol", roleLabel, color: "indigo");

                if (!string.IsNullOrWhiteSpace(deptName))
                    eb.AddTextWidget("w_dept", "Departman", deptName, color: "slate");
                if (!string.IsNullOrWhiteSpace(u.EmployeeCode))
                    eb.AddTextWidget("w_emp", "Personel Kodu", u.EmployeeCode, color: "slate");
                if (u.SupervisorUserId.HasValue && supervisorLookup.TryGetValue(u.SupervisorUserId.Value, out var supName))
                    eb.AddTextWidget("w_sup", "Amir", supName, color: "blue");
                if (!string.IsNullOrWhiteSpace(u.PhoneNumber))
                    eb.AddPhoneWidget("w_phone", "Telefon", u.PhoneNumber, color: "emerald");

                eb.WithPrimaryAction(
                    label: "Düzenle",
                    icon: "Edit",
                    url: $"#edit-user-{u.Id}",
                    color: "amber",
                    hideButton: true);

                eb.WithSecondaryAction(
                    label: "Sil",
                    icon: "Trash2",
                    apiUrl: $"/CompanyUser/Delete?id={u.Id}",
                    apiMethod: "POST",
                    confirm: $"Bu kullanıcıyı silmek istediğinize emin misiniz? ({u.FullName})");

                eb.AddExtraAction(
                    icon: "KeyRound",
                    color: "violet",
                    tooltip: "�?ifre Sıfırla",
                    type: "api-post",
                    apiUrl: $"/CompanyUser/ResetPassword?id={u.Id}",
                    confirm: "�?ifreyi 12345678 olarak sıfırlamak istediğinize emin misiniz?");

                return eb;
            })
            .Build();
    }

    // Basit telefon normalizasyonu — bosluklari/parantez/tire'yi temizle, "+" korunur.
    // Uzun veya bos string null'a duser.
    private static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (cleaned.Length == 0) return null;
        if (cleaned.Length > 30) cleaned = cleaned[..30];
        return cleaned;
    }

    private static string GetRoleLabel(UserRole role) => role switch
    {
        UserRole.SystemAdmin => "Sistem Admin",
        UserRole.DepartmentManager => "Admin",
        UserRole.Operator => "User",
        // Eski enum degerleri — yeni kayitlarda secilemiyor ama mevcut data icin gosterim:
        UserRole.Approver => "Onaylayıcı",
        UserRole.Auditor => "Denetçi",
        _ => role.ToString()
    };

    // ── Form data ─────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetFormData(CancellationToken ct)
    {
        var companyId = GetCurrentCompanyId();

        var deptsAll = await _deptRepo.GetAllAsync(ct);
        var usersAll = await _userRepo.GetAllAsync(ct);

        var departments = deptsAll
            .Where(d => d.IsActive && d.CompanyId == companyId)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new { id = d.Id, name = d.Name })
            .ToArray();

        var supervisors = usersAll
            .Where(u => u.IsActive && u.CompanyId == companyId)
            .OrderBy(u => u.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(u => new { id = u.Id, name = u.FullName, email = u.Email })
            .ToArray();

        // Yetki yükseltme koruması: "Sistem Admin" seçeneği yalnızca SystemAdmin'e gösterilir.
        var canAssignSysAdmin = IsSystemAdmin();
        var roles = new[]
        {
            new { value = (int)UserRole.Operator,          label = "User",         sysOnly = false },
            new { value = (int)UserRole.DepartmentManager, label = "Admin",        sysOnly = false },
            new { value = (int)UserRole.SystemAdmin,       label = "Sistem Admin", sysOnly = true  },
        }
        .Where(r => !r.sysOnly || canAssignSysAdmin)
        .Select(r => new { r.value, r.label })
        .ToArray();

        return Json(new { departments, supervisors, roles });
    }

    // ── Aktif kullanıcı lookup (Personel kartı "Sistem Kullanıcısı" eşleştirme dropdown'ı) ──
    [HttpGet]
    public async Task<IActionResult> UsersLookup(CancellationToken ct)
    {
        var companyId = GetCurrentCompanyId();
        // 2026-06-08 — departmentId eklendi.
        // 2026-06-09 — departmentName eklendi: Yetki ekranı topbar'ında seçili kullanıcının departmanı gösterilir.
        var depts = (await _deptRepo.GetAllAsync(ct))
            .ToDictionary(d => d.Id, d => d.Name);
        var users = (await _userRepo.GetAllAsync(ct))
            .Where(u => u.IsActive && u.CompanyId == companyId)
            .OrderBy(u => u.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(u => new {
                id           = u.Id,
                name         = u.FullName,
                email        = u.Email,
                departmentId = u.DepartmentId,
                departmentName = u.DepartmentId.HasValue && depts.TryGetValue(u.DepartmentId.Value, out var dn) ? dn : (string?)null,
            })
            .ToArray();
        return Json(users);
    }

    [HttpGet]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var companyId = GetCurrentCompanyId();
        var user = await _userRepo.GetByIdAsync(id, ct);
        if (user is null || user.CompanyId != companyId)
            return Json(new { ok = false, error = "Kullanıcı bulunamadı." });

        return Json(new
        {
            ok = true,
            user = new
            {
                id = user.Id,
                companyId = user.CompanyId,
                fullName = user.FullName,
                email = user.Email,
                employeeCode = user.EmployeeCode,
                departmentId = user.DepartmentId,
                supervisorUserId = user.SupervisorUserId,
                phoneNumber = user.PhoneNumber,
                role = (int)user.Role,
                isActive = user.IsActive,
            }
        });
    }

    // ── Save (Create / Update) ────────────────────────────────────────────
    public sealed record SaveCompanyUserRequest(
        int? Id,
        string FullName,
        string Email,
        string? EmployeeCode,
        int? DepartmentId,
        int? SupervisorUserId,
        string? PhoneNumber,
        int Role,
        bool IsActive,
        string? Password = null);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SaveCompanyUserRequest dto, CancellationToken ct)
    {
        if (dto is null)
            return Json(new { ok = false, error = "Geçersiz istek." });

        // Hedef şirket her zaman oturumdaki şirket — çapraz şirket atama Sistem Yönetimi'nden yapılır.
        var companyId = GetCurrentCompanyId();

        if (companyId <= 0)
            return Json(new { ok = false, error = "�?irket bilgisi bulunamadı." });

        if (!Enum.IsDefined(typeof(UserRole), dto.Role))
            return Json(new { ok = false, error = "Geçersiz yetki." });
        var role = (UserRole)dto.Role;
        // 2026-05-26: Bu ekranda yalniz "User" (Operator) / "Admin" (DepartmentManager) /
        // "Sistem Admin" (SystemAdmin) secilebilir. Diger eski enum degerleri (Approver/Auditor)
        // reddedilir — onlar gelecek rol planinda yer alacak.
        if (role != UserRole.Operator && role != UserRole.DepartmentManager && role != UserRole.SystemAdmin)
            return Json(new { ok = false, error = "Yalnızca User, Admin veya Sistem Admin yetkisi atanabilir." });

        // Yetki yükseltme koruması: "Sistem Admin" yetkisini yalnızca bir Sistem Admini atayabilir.
        // (Aksi halde bir Admin kendini/başkasını SystemAdmin yaparak yetki yükseltebilir.)
        if (role == UserRole.SystemAdmin && !IsSystemAdmin())
            return Json(new { ok = false, error = "\"Sistem Admin\" yetkisini yalnızca bir Sistem Admini atayabilir." });

        try
        {
            if (dto.Id.HasValue && dto.Id.Value > 0)
            {
                // ── Update ─────────────────────────────────────────────
                var existing = await _userRepo.GetByIdAsync(dto.Id.Value, ct);
                if (existing is null || existing.CompanyId != companyId)
                    return Json(new { ok = false, error = "Kullanıcı bulunamadı." });

                // Yetki yükseltme koruması: mevcut bir Sistem Admini kaydını yalnızca Sistem Admini düzenleyebilir.
                if (existing.Role == UserRole.SystemAdmin && !IsSystemAdmin())
                    return Json(new { ok = false, error = "Sistem Admini kullanıcıları yalnızca bir Sistem Admini düzenleyebilir." });

                // 2026-06-08 — Departman artık UI'da; dto.DepartmentId ile güncellenir.
                // 2026-06-15 — Amir artık UI'da; dto.SupervisorUserId ile güncellenir.
                await _adminService.UpdateUserAsync(new UpdateUserRequest(
                    Id: existing.Id,
                    CompanyId: companyId,
                    FullName: dto.FullName ?? string.Empty,
                    Email: dto.Email ?? string.Empty,
                    Password: null,
                    SetRole: true,
                    Role: role
                ), ct);

                // AdminManagementService.UpdateUserAsync supervisor / phone / department / isActive
                // degisikligini kabul etmiyor — repository uzerinden yeni shape build edip tek UPDATE'le uygula.
                var refreshed = await _userRepo.GetByIdAsync(existing.Id, ct);
                if (refreshed is not null)
                {
                    var rebuilt = new Domain.Entities.UserProfile
                    {
                        Id               = refreshed.Id,
                        CompanyId        = refreshed.CompanyId,
                        FullName         = refreshed.FullName,
                        Email            = refreshed.Email,
                        EmployeeCode     = refreshed.EmployeeCode,
                        // 2026-06-08 — DepartmentId UI payload'unda mevcut. Boş gönderilirse
                        // (null) departmandan çıkar; dolu ise yeni departmanı set et.
                        DepartmentId     = dto.DepartmentId,
                        SupervisorUserId = dto.SupervisorUserId,
                        PhoneNumber      = NormalizePhone(dto.PhoneNumber),
                        Role             = refreshed.Role,              // role yukarida AdminService set etti
                        Permissions      = refreshed.Permissions,
                    };
                    rebuilt.SetPasswordHash(refreshed.PasswordHash);
                    rebuilt.SetInterfacePreferences(refreshed.LanguageCode, refreshed.ThemeCode);
                    rebuilt.SetGridPreferencesJson(refreshed.GridPreferencesJson);
                    if (!dto.IsActive) rebuilt.Deactivate();
                    // dto.IsActive=true ise yeni instance'in default IsActive=true zaten.
                    await _userRepo.UpdateAsync(rebuilt, ct);

                    // İşlem logu — AdminService diff'i FullName/Email/Rol'ü kapsar; bu ikinci
                    // UPDATE'in taşıdığı alanlar (departman/amir/telefon/aktiflik) burada diff'lenir.
                    try
                    {
                        _audit.LogUpdate("User", existing.Id, rebuilt.Email,
                            new { existing.DepartmentId, existing.SupervisorUserId, existing.PhoneNumber, existing.IsActive },
                            new { rebuilt.DepartmentId, rebuilt.SupervisorUserId, rebuilt.PhoneNumber, rebuilt.IsActive });
                    }
                    catch { /* audit yazımı kaydı asla bozmaz */ }
                }

                return Json(new { ok = true, id = existing.Id, message = "Kullanıcı güncellendi." });
            }
            else
            {
                // ── Create ─────────────────────────────────────────────
                // EmployeeCode bos ise email'den turet (kullanici kod girmek zorunda olmasin).
                var employeeCode = string.IsNullOrWhiteSpace(dto.EmployeeCode)
                    ? (dto.Email ?? string.Empty).Trim().Split('@')[0]
                    : dto.EmployeeCode.Trim();
                if (string.IsNullOrWhiteSpace(employeeCode))
                    employeeCode = $"USER-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant()}";

                var permissions = UserAuthorizationCatalog.GetAllowedPermissions(role).ToArray();
                if (permissions.Length == 0)
                {
                    return Json(new { ok = false, error = "Bu rol için geçerli yetki tanımlı değil." });
                }

                // Ilk giris sifresi: form'dan gelirse onu kullan, yoksa DefaultPassword.
                var initialPassword = string.IsNullOrWhiteSpace(dto.Password)
                    ? DefaultPassword
                    : dto.Password.Trim();
                if (initialPassword.Length < 6)
                    return Json(new { ok = false, error = "�?ifre en az 6 karakter olmalı." });

                await _adminService.CreateUserAsync(new CreateUserRequest(
                    CompanyId: companyId,
                    FullName: dto.FullName ?? string.Empty,
                    Email: dto.Email ?? string.Empty,
                    EmployeeCode: employeeCode,
                    DepartmentId: dto.DepartmentId,
                    SupervisorUserId: dto.SupervisorUserId,
                    Role: role,
                    Permissions: permissions,
                    Password: initialPassword
                ), ct);

                // PhoneNumber CreateUserRequest'te yok — telefon verildiyse yeni user'i
                // bulup repository uzerinden update'le set ediyoruz.
                var phone = NormalizePhone(dto.PhoneNumber);
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    var created = await _userRepo.GetByEmailAndCompanyIdAsync(dto.Email ?? string.Empty, companyId, ct);
                    if (created is not null)
                    {
                        var withPhone = new Domain.Entities.UserProfile
                        {
                            Id               = created.Id,
                            CompanyId        = created.CompanyId,
                            FullName         = created.FullName,
                            Email            = created.Email,
                            EmployeeCode     = created.EmployeeCode,
                            DepartmentId     = created.DepartmentId,
                            SupervisorUserId = created.SupervisorUserId,
                            PhoneNumber      = phone,
                            Role             = created.Role,
                            Permissions      = created.Permissions,
                        };
                        withPhone.SetPasswordHash(created.PasswordHash);
                        withPhone.SetInterfacePreferences(created.LanguageCode, created.ThemeCode);
                        withPhone.SetGridPreferencesJson(created.GridPreferencesJson);
                        if (!created.IsActive) withPhone.Deactivate();
                        await _userRepo.UpdateAsync(withPhone, ct);
                    }
                }

                var msg = string.IsNullOrWhiteSpace(dto.Password)
                    ? $"Kullanıcı oluşturuldu. Varsayılan şifre: {DefaultPassword}"
                    : "Kullanıcı oluşturuldu. Belirlediğiniz şifre ile giriş yapılabilir.";
                return Json(new { ok = true, message = msg });
            }
        }
        catch (ArgumentException ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ── Delete (soft) ─────────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var (companyId, currentUserId) = GetCurrentUser();
        var currentEmail = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

        var user = await _userRepo.GetByIdAsync(id, ct);
        if (user is null || user.CompanyId != companyId)
            return Json(new { ok = false, error = "Kullanıcı bulunamadı." });

        // 2026-06-06: Self-delete guard — hem ID hem Email karşılaştırması.
        // Eski auth cookie'de NameIdentifier Guid olabilir → int.TryParse fail
        // → currentUserId=0 olur ve "ID == 0 ID" yanlış pozitif veriyordu.
        // Email karşılaştırması cookie versiyonundan bağımsız çalışır.
        var isSelfById    = currentUserId > 0 && user.Id == currentUserId;
        var isSelfByEmail = !string.IsNullOrWhiteSpace(currentEmail) &&
                            string.Equals(user.Email, currentEmail, StringComparison.OrdinalIgnoreCase);
        if (isSelfById || isSelfByEmail)
            return Json(new { ok = false, error = "Kendi hesabınızı silemezsiniz." });

        try
        {
            user.Deactivate();
            await _userRepo.UpdateAsync(user, ct);

            // İşlem logu — kullanıcı silme = deaktifleştirme (soft-delete)
            _audit.LogDelete("User", id, user.Email, detail: user.FullName);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ── Sifre sifirlama ───────────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int id, CancellationToken ct)
    {
        var companyId = GetCurrentCompanyId();
        var user = await _userRepo.GetByIdAsync(id, ct);
        if (user is null || user.CompanyId != companyId)
            return Json(new { ok = false, error = "Kullanıcı bulunamadı." });

        try
        {
            user.SetPasswordHash(_passwordHashService.HashPassword(DefaultPassword));
            await _userRepo.UpdateAsync(user, ct);
            return Json(new { ok = true, message = $"�?ifre sıfırlandı: {DefaultPassword}" });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }
}
