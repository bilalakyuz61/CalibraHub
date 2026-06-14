using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Models.Account;

public sealed class LoginInputModel
{
    [Required(ErrorMessage = "Sirket secimi zorunludur.")]
    public int? CompanyId { get; set; }

    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Gecerli bir e-posta giriniz.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Sifre zorunludur.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public IReadOnlyCollection<SelectListItem> CompanyOptions { get; set; } = Array.Empty<SelectListItem>();
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
    /// <summary>Login sayfasındaki tema toggle'ından gelen tercih (dark|light). Boşsa değiştirilmez.</summary>
    public string? ThemeCode { get; set; }
}
