using System.ComponentModel.DataAnnotations;

namespace CalibraHub.Web.Models.Account;

public sealed class ForgotPasswordInputModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
