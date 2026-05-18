using System.ComponentModel.DataAnnotations;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Shared;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Models.Admin;

public sealed class DepartmentManagementViewModel
{
    public required IReadOnlyCollection<DepartmentDto> Departments { get; init; }
    public required IReadOnlyCollection<SelectListItem> CompanyOptions { get; init; }
    public required GridListStateViewModel ListState { get; init; }
    public DepartmentCreateInput Input { get; init; } = new();

    /// <summary>SmartBoard (C-Grid) konfigurasyonu — Personel ekraniyla ayni kontrat.</summary>
    public object? BoardConfig { get; init; }
}

public sealed class DepartmentCreateInput
{
    [Required(ErrorMessage = "Sirket secimi zorunludur.")]
    public int? CompanyId { get; set; }

    // Code UI'dan kaldirildi — name'den auto-uretilir. Backward compat icin tutuluyor.
    [MaxLength(20, ErrorMessage = "Departman kodu en fazla 20 karakter olabilir.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Departman adi zorunludur.")]
    [MaxLength(100, ErrorMessage = "Departman adi en fazla 100 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;
}

public sealed class DepartmentUpdateInput
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
