using System.ComponentModel.DataAnnotations;

namespace CalibraHub.Web.Models.Account;

public sealed class ChangePasswordInputModel
{
    [Required(ErrorMessage = "Mevcut sifre zorunludur.")]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni sifre zorunludur.")]
    [MinLength(8, ErrorMessage = "Yeni sifre en az 8 karakter olmalidir.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni sifre tekrar zorunludur.")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Sifreler uyusmuyor.")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}
