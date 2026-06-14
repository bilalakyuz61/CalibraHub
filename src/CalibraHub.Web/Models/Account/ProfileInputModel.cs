using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Models.Account;

/// <summary>
/// Kullanıcı kendi profilini düzenler. Email + Role + IsActive değiştirilemez —
/// güvenlik amaçlı kullanıcı kendi e-postasını veya rolünü değiştirip yetki
/// yükseltemez. Email kimlik birincil anahtarı, role admin tarafından yönetilir.
/// </summary>
public sealed class ProfileInputModel
{
    // Read-only display — view'da disabled input olarak gösterilir
    public string Email { get; set; } = string.Empty;
    public string RoleLabel { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;

    // Düzenlenebilir alanlar
    [Required(ErrorMessage = "Ad soyad zorunludur.")]
    [StringLength(100, ErrorMessage = "Ad soyad en fazla 100 karakter olabilir.")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(30, ErrorMessage = "Personel kodu en fazla 30 karakter olabilir.")]
    public string? EmployeeCode { get; set; }

    public int? DepartmentId { get; set; }
    public int? SupervisorUserId { get; set; }

    [StringLength(30, ErrorMessage = "Telefon en fazla 30 karakter olabilir.")]
    public string? PhoneNumber { get; set; }

    [Required] public string LanguageCode { get; set; } = "tr-TR";
    [Required] public string ThemeCode { get; set; } = "light";

    // Dropdown beslemeleri (view tarafında render için)
    public List<SelectListItem> Departments { get; set; } = new();
    public List<SelectListItem> Supervisors { get; set; } = new();
}
