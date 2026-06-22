using System.ComponentModel.DataAnnotations;

namespace CalibraHub.Web.Models.Account;

public sealed class ResetPasswordInputModel
{
    public string Token { get; set; } = string.Empty;

    [Required]
    [MinLength(10)]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(NewPassword), ErrorMessage = "Şifreler eşleşmiyor.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
