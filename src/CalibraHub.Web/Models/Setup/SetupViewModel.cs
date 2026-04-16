using System.ComponentModel.DataAnnotations;

namespace CalibraHub.Web.Models.Setup;

public sealed class SetupViewModel
{
    [Required(ErrorMessage = "Sirket adi zorunludur.")]
    [MaxLength(120)]
    public string CompanyName { get; set; } = string.Empty;

    // SQL bağlantı bileşenleri — boş bırakılırsa sistem DB kullanılır
    [MaxLength(200)]
    public string? SqlServer { get; set; }

    [MaxLength(100)]
    public string? SqlDatabase { get; set; }

    [MaxLength(100)]
    public string? SqlUsername { get; set; }

    [MaxLength(100)]
    public string? SqlPassword { get; set; }

    [Required(ErrorMessage = "Ad soyad zorunludur.")]
    [MaxLength(200)]
    public string AdminFullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Gecerli bir e-posta giriniz.")]
    [MaxLength(200)]
    public string AdminEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Sifre zorunludur.")]
    [MinLength(8, ErrorMessage = "Sifre en az 8 karakter olmalidir.")]
    public string AdminPassword { get; set; } = string.Empty;
}
