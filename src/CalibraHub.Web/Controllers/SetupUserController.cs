using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// SetupUserController — Sirket ve Kullanici Tanimlari (SystemAdmin-only).
///
/// CompanyUserController'in SystemAdmin esdegeri: tum sirketlerdeki kullanicilari
/// listeler/yonetir. Modal formu CompanyUser ile birebir aynidir, sadece ust
/// kisma SystemAdmin'in kullaniciyi atayacagi "Sirket" dropdown'u eklenmistir.
///
/// Auth: [AllowAnonymous] + [GateProtected] (2026-05-26 update):
/// Bu ekran artik login menusunden degil, Login → "Sistem Ayarlari" linki ile erisilir.
/// TOTP gate password girilince Gate/Dashboard'a duser; oradan "Kullanici Tanimlari"
/// butonu /SetupUser'i acar. Sirket tanimlari da ayni hub'dan (Setup/Companies).
/// CompanyId client'tan kabul edilir (SystemAdmin tum sirketleri yonettigi icin guvenli).
///
/// View ile cakismadan ayri localStorage scope: boardKey = "setup-users".
/// CSS prefix: su-* (CompanyUser cu-* ile cakismaz).
/// </summary>
// 2026-05-26 v2: Sistem-seviyesi ekran — Login → "Sistem Ayarlari" → TOTP gate
// arkasinda erisilir. App icindeki menuden cikarildi (Kullanici Tanimlamalari
// ile karistiriliyordu). GateProtected TOTP unlock'i ister; SetupController
// pattern'iyle birebir.
[AllowAnonymous]
[GateProtected]
[IgnoreAntiforgeryToken]
public sealed class SetupUserController : Controller
{
    private const string DefaultPassword = "12345678";

    private readonly IUserProfileRepository _userRepo;
    private readonly ICompanyRepository _companyRepo;
    private readonly IDepartmentRepository _deptRepo;
    private readonly IAdminManagementService _adminService;
    private readonly IPasswordHashService _passwordHashService;

    public SetupUserController(
        IUserProfileRepository userRepo,
        ICompanyRepository companyRepo,
        IDepartmentRepository deptRepo,
        IAdminManagementService adminService,
        IPasswordHashService passwordHashService)
    {
        _userRepo = userRepo;
        _companyRepo = companyRepo;
        _deptRepo = deptRepo;
        _adminService = adminService;
        _passwordHashService = passwordHashService;
    }

    // 2026-05-26: RequireAuth() devre disi — [GateProtected] TOTP zaten yetkilendirmeyi
    // saglar. SetupController ile ayni davranis.
    private IActionResult? RequireAuth() => null;

    private int GetCurrentUserId()
    {
        var s = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        int.TryParse(s, out var id);
        return id;
    }

    // ── Liste sayfasi ─────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";

        var config = await BuildBoardConfigAsync(ct);
        ViewData["Title"] = "�?irket ve Kullanıcı Tanımları";
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
        var auth = RequireAuth();
        if (auth != null) return auth;

        var config = await BuildBoardConfigAsync(ct);
        return Json(config, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var allUsers = (await _userRepo.GetAllAsync(ct))
            .OrderBy(u => u.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var companies = (await _companyRepo.GetAllAsync(ct))
            .ToDictionary(c => c.Id, c => c.Name);

        var departments = (await _deptRepo.GetAllAsync(ct))
            .ToDictionary(d => d.Id, d => d.Name);

        var userLookup = allUsers.ToDictionary(u => u.Id, u => u.FullName);

        return CalibraHub.Application.SmartBoard.SmartBoard.For(allUsers)
            .WithBoardKey("setup-users")
            .WithTitle("�?irket ve Kullanıcı Tanımları", subtitle: $"{allUsers.Count} kullanıcı")
            .WithIcon("UserCog", "indigo")
            .WithRefreshUrl("/SetupUser/BoardConfig")
            .WithSearchPlaceholder("Ad, e-posta, şirket…")
            .WithEmptyText("Henüz kullanıcı tanımlanmamış")
            .AddHeaderAction("new", "Yeni Kullanıcı", "Plus", "#open-new-user-modal")
            .MapEntities(u =>
            {
                var roleLabel = GetRoleLabel(u.Role);
                var companyName = companies.TryGetValue(u.CompanyId, out var cn) ? cn : $"�?irket #{u.CompanyId}";
                var deptName = u.DepartmentId.HasValue && departments.TryGetValue(u.DepartmentId.Value, out var dn) ? dn : null;

                var eb = CalibraHub.Application.SmartBoard.SmartBoardEntity
                    .For(u.Id.ToString(), u.FullName, subtitle: u.Email)
                    .WithDescription($"{companyName} • {roleLabel}")
                    .WithStatusBadge(u.IsActive ? "Aktif" : "Pasif", u.IsActive ? "emerald" : "slate")
                    .AddTextWidget("w_company", "�?irket", companyName, color: "violet")
                    .AddTextWidget("w_role", "Rol", roleLabel, color: "indigo");

                if (!string.IsNullOrWhiteSpace(deptName))
                    eb.AddTextWidget("w_dept", "Departman", deptName, color: "slate");
                if (!string.IsNullOrWhiteSpace(u.EmployeeCode))
                    eb.AddTextWidget("w_emp", "Personel Kodu", u.EmployeeCode, color: "slate");
                if (u.SupervisorUserId.HasValue && userLookup.TryGetValue(u.SupervisorUserId.Value, out var supName))
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
                    apiUrl: $"/SetupUser/Delete?id={u.Id}",
                    apiMethod: "POST",
                    confirm: $"Bu kullanıcıyı silmek istediğinize emin misiniz? ({u.FullName})");

                eb.AddExtraAction(
                    icon: "KeyRound",
                    color: "violet",
                    tooltip: "�?ifre Sıfırla",
                    type: "api-post",
                    apiUrl: $"/SetupUser/ResetPassword?id={u.Id}",
                    confirm: "�?ifreyi 12345678 olarak sıfırlamak istediğinize emin misiniz?");

                return eb;
            })
            .Build();
    }

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
        UserRole.Approver => "Onaylayıcı",
        UserRole.Auditor => "Denetçi",
        _ => role.ToString()
    };

    // ── Form data ─────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetFormData(int? companyId, CancellationToken ct)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        var companies = (await _companyRepo.GetAllAsync(ct))
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new { id = c.Id, name = c.Name })
            .ToArray();

        // Departments / supervisors — sirket secimine bagli filtrelenir (companyId verildiyse).
        // CompanyId yoksa hepsi doner; frontend gerektiginde GetFormData?companyId=X ile yeniler.
        var deptsAll = await _deptRepo.GetAllAsync(ct);
        var usersAll = await _userRepo.GetAllAsync(ct);
        var departments = deptsAll
            .Where(d => d.IsActive && (!companyId.HasValue || d.CompanyId == companyId.Value))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new { id = d.Id, name = d.Name, companyId = d.CompanyId })
            .ToArray();
        var supervisors = usersAll
            .Where(u => u.IsActive && (!companyId.HasValue || u.CompanyId == companyId.Value))
            .OrderBy(u => u.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(u => new { id = u.Id, name = u.FullName, companyId = u.CompanyId })
            .ToArray();

        var roles = new[]
        {
            new { value = (int)UserRole.Operator,          label = "User" },
            new { value = (int)UserRole.DepartmentManager, label = "Admin" },
            new { value = (int)UserRole.SystemAdmin,       label = "Sistem Admin" },
        };

        return Json(new { companies, departments, supervisors, roles });
    }

    [HttpGet]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        var user = await _userRepo.GetByIdAsync(id, ct);
        if (user is null)
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
    public sealed record SaveSetupUserRequest(
        int? Id,
        int CompanyId,            // SystemAdmin ekraninda ZORUNLU — kullanicinin atanacagi sirket
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
    public async Task<IActionResult> Save([FromBody] SaveSetupUserRequest dto, CancellationToken ct)
    {
        var auth = RequireAuth();
        if (auth != null) return auth;

        if (dto is null)
            return Json(new { ok = false, error = "Geçersiz istek." });
        if (dto.CompanyId <= 0)
            return Json(new { ok = false, error = "�?irket seçimi zorunludur." });

        if (!Enum.IsDefined(typeof(UserRole), dto.Role))
            return Json(new { ok = false, error = "Geçersiz yetki." });
        var role = (UserRole)dto.Role;
        if (role != UserRole.Operator && role != UserRole.DepartmentManager && role != UserRole.SystemAdmin)
            return Json(new { ok = false, error = "Yalnızca User, Admin veya Sistem Admin yetkisi atanabilir." });

        try
        {
            if (dto.Id.HasValue && dto.Id.Value > 0)
            {
                // ── Update ─────────────────────────────────────────────
                var existing = await _userRepo.GetByIdAsync(dto.Id.Value, ct);
                if (existing is null)
                    return Json(new { ok = false, error = "Kullanıcı bulunamadı." });

                var supervisorId = dto.SupervisorUserId;
                if (supervisorId.HasValue && supervisorId.Value == existing.Id) supervisorId = null;

                await _adminService.UpdateUserAsync(new UpdateUserRequest(
                    Id: existing.Id,
                    CompanyId: dto.CompanyId,
                    FullName: dto.FullName ?? string.Empty,
                    Email: dto.Email ?? string.Empty,
                    Password: null,
                    SetRole: true,
                    Role: role
                ), ct);

                // Phone / Department / Supervisor / IsActive — repository ile direkt apply
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
                        DepartmentId     = dto.DepartmentId,
                        SupervisorUserId = supervisorId,
                        PhoneNumber      = NormalizePhone(dto.PhoneNumber),
                        Role             = refreshed.Role,
                        Permissions      = refreshed.Permissions,
                    };
                    rebuilt.SetPasswordHash(refreshed.PasswordHash);
                    rebuilt.SetInterfacePreferences(refreshed.LanguageCode, refreshed.ThemeCode);
                    rebuilt.SetGridPreferencesJson(refreshed.GridPreferencesJson);
                    if (!dto.IsActive) rebuilt.Deactivate();
                    await _userRepo.UpdateAsync(rebuilt, ct);
                }

                return Json(new { ok = true, id = existing.Id, message = "Kullanıcı güncellendi." });
            }
            else
            {
                // ── Create ─────────────────────────────────────────────
                var employeeCode = string.IsNullOrWhiteSpace(dto.EmployeeCode)
                    ? (dto.Email ?? string.Empty).Trim().Split('@')[0]
                    : dto.EmployeeCode.Trim();
                if (string.IsNullOrWhiteSpace(employeeCode))
                    employeeCode = $"USER-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant()}";

                var permissions = UserAuthorizationCatalog.GetAllowedPermissions(role).ToArray();
                if (permissions.Length == 0)
                    return Json(new { ok = false, error = "Bu rol için geçerli yetki tanımlı değil." });

                var initialPassword = string.IsNullOrWhiteSpace(dto.Password)
                    ? DefaultPassword
                    : dto.Password.Trim();
                if (initialPassword.Length < 6)
                    return Json(new { ok = false, error = "�?ifre en az 6 karakter olmalı." });

                await _adminService.CreateUserAsync(new CreateUserRequest(
                    CompanyId: dto.CompanyId,
                    FullName: dto.FullName ?? string.Empty,
                    Email: dto.Email ?? string.Empty,
                    EmployeeCode: employeeCode,
                    DepartmentId: dto.DepartmentId,
                    SupervisorUserId: dto.SupervisorUserId,
                    Role: role,
                    Permissions: permissions,
                    Password: initialPassword
                ), ct);

                // PhoneNumber CreateUserRequest'te yok — telefon verildiyse repository update.
                var phone = NormalizePhone(dto.PhoneNumber);
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    var created = await _userRepo.GetByEmailAndCompanyIdAsync(dto.Email ?? string.Empty, dto.CompanyId, ct);
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
        var auth = RequireAuth();
        if (auth != null) return auth;

        var currentUserId = GetCurrentUserId();
        if (id == currentUserId)
            return Json(new { ok = false, error = "Kendi hesabınızı silemezsiniz." });

        var user = await _userRepo.GetByIdAsync(id, ct);
        if (user is null)
            return Json(new { ok = false, error = "Kullanıcı bulunamadı." });

        try
        {
            user.Deactivate();
            await _userRepo.UpdateAsync(user, ct);
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
        var auth = RequireAuth();
        if (auth != null) return auth;

        var user = await _userRepo.GetByIdAsync(id, ct);
        if (user is null)
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
