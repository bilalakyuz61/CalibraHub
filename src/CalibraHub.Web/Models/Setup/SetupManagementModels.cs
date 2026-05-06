using System.ComponentModel.DataAnnotations;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Shared;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Models.Setup;

// ── Şirket Tanımı (basit) ────────────────────────────────────────────────────

public sealed class SetupCompanyInput
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Sirket adi zorunludur.")]
    [MaxLength(120, ErrorMessage = "Sirket adi en fazla 120 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    // SQL bağlantı bileşenleri — boş bırakılırsa sistem DB kullanılır
    [MaxLength(200)]
    public string? SqlServer { get; set; }

    [MaxLength(100)]
    public string? SqlDatabase { get; set; }

    [MaxLength(100)]
    public string? SqlUsername { get; set; }

    [MaxLength(100)]
    public string? SqlPassword { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class SetupCompanyViewModel
{
    public required IReadOnlyCollection<CompanyDto> Companies { get; init; }
    public required GridListStateViewModel ListState { get; init; }
    public SetupCompanyInput Input { get; init; } = new();
}

// ── Birleşik Tanım Ekranı ────────────────────────────────────────────────────

public sealed class SetupDefinitionsViewModel
{
    // Şirket bölümü
    public required IReadOnlyCollection<CompanyDto> Companies { get; init; }
    public required GridListStateViewModel CompanyListState { get; init; }
    public SetupCompanyInput CompanyInput { get; init; } = new();

    // Kullanıcı bölümü
    public required IReadOnlyCollection<UserProfileDto> Users { get; init; }
    public required IReadOnlyCollection<SelectListItem> CompanyOptions { get; init; }
    public required GridListStateViewModel UserListState { get; init; }
    public SetupUserInput UserInput { get; init; } = new();

    /// "companies" veya "users" — sekme durumunu korur
    public string ActiveTab { get; init; } = "companies";
}

// ── Kullanıcı Tanımı (basit) ─────────────────────────────────────────────────

public sealed class SetupUserInput
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Sirket secimi zorunludur.")]
    public int? CompanyId { get; set; }

    [Required(ErrorMessage = "Ad zorunludur.")]
    [MaxLength(60, ErrorMessage = "Ad en fazla 60 karakter olabilir.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Soyad zorunludur.")]
    [MaxLength(60, ErrorMessage = "Soyad en fazla 60 karakter olabilir.")]
    public string LastName { get; set; } = string.Empty;

    public string FullName => $"{FirstName} {LastName}".Trim();

    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Gecerli bir e-posta giriniz.")]
    [MaxLength(120, ErrorMessage = "E-posta en fazla 120 karakter olabilir.")]
    public string Email { get; set; } = string.Empty;

    [MinLength(8, ErrorMessage = "Sifre en az 8 karakter olmalidir.")]
    public string? Password { get; set; }

    /// <summary>
    /// CalibraHub yetki seviyesi: "Admin" / "SistemAdmin" / "User" (case-insensitive).
    /// Mapping → UserRole enum:
    ///   • "Admin"        → DepartmentManager (sirket admini, dashboards tasarlar, dokumanlari yonetir)
    ///   • "SistemAdmin"  → SystemAdmin (tum yetkiler — admin@calibra.local gibi)
    ///   • "User"         → Operator (temel goruntuleme yetkileri)
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Grafana yetki seviyesi: NULL/empty = Grafana'ya eklenmez,
    /// "Viewer" / "Designer" / "Admin" → ilgili rolde Calibra_{companyId} org'a eklenir.
    /// Update sirasinda da uygulanir (mevcut rol farkli ise update, NULL'a inerse cikarilir).
    /// </summary>
    public string? GrafanaRole { get; set; }
}

public sealed class SetupUserViewModel
{
    public required IReadOnlyCollection<UserProfileDto> Users { get; init; }
    public required IReadOnlyCollection<SelectListItem> CompanyOptions { get; init; }
    public required GridListStateViewModel ListState { get; init; }
    public SetupUserInput Input { get; init; } = new();
}
