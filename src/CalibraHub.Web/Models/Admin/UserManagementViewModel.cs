using System.ComponentModel.DataAnnotations;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Shared;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Models.Admin;

public sealed class UserManagementViewModel
{
    public required IReadOnlyCollection<UserProfileDto> Users { get; init; }
    public required IReadOnlyCollection<SelectListItem> CompanyOptions { get; init; }
    public required IReadOnlyCollection<SelectListItem> DepartmentOptions { get; init; }
    public required IReadOnlyCollection<SelectListItem> SupervisorOptions { get; init; }
    public required IReadOnlyCollection<SelectListItem> RoleOptions { get; init; }
    public required IReadOnlyCollection<PermissionOptionViewModel> PermissionOptions { get; init; }
    public required GridListStateViewModel ListState { get; init; }
    public UserCreateInput Input { get; init; } = new();
}

public sealed class PermissionOptionViewModel
{
    public required string Value { get; init; }
    public required string Label { get; init; }
    public bool IsSelected { get; init; }
}

public sealed class UserCreateInput
{
    [Required(ErrorMessage = "Sirket secimi zorunludur.")]
    public int? CompanyId { get; set; }

    [Required(ErrorMessage = "Ad soyad zorunludur.")]
    [MaxLength(100, ErrorMessage = "Ad soyad en fazla 100 karakter olabilir.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Gecerli bir e-posta giriniz.")]
    [MaxLength(120, ErrorMessage = "E-posta en fazla 120 karakter olabilir.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Sicil kodu zorunludur.")]
    [MaxLength(30, ErrorMessage = "Sicil kodu en fazla 30 karakter olabilir.")]
    public string EmployeeCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Departman secimi zorunludur.")]
    public Guid? DepartmentId { get; set; }

    public Guid? SupervisorUserId { get; set; }

    [Required(ErrorMessage = "Rol secimi zorunludur.")]
    public string Role { get; set; } = string.Empty;

    [MinLength(1, ErrorMessage = "En az bir yetki seciniz.")]
    public List<string> Permissions { get; set; } = new();
}
