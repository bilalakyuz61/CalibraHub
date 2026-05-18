using System.ComponentModel.DataAnnotations;
using CalibraHub.Web.Models.Shared;

namespace CalibraHub.Web.Models.Logistics;

public sealed class MeasureUnitsSmartBoardViewModel
{
    public object? BoardConfig { get; init; }
}

public sealed class UnitEditViewModel
{
    public int? Id { get; init; }
    public string? UnitCode { get; init; }
    public string? UnitName { get; init; }
    public string? IntlCode { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class UnitsViewModel
{
    public required IReadOnlyCollection<UnitRowViewModel> Definitions { get; init; }
    public required IReadOnlyCollection<ScreenLayoutTabViewModel> LayoutTabs { get; init; }
    public required GridListStateViewModel ListState { get; init; }
    public UnitInput Input { get; init; } = new();
}

public sealed class UnitRowViewModel
{
    public int Id { get; init; }
    public string UnitCode { get; init; } = string.Empty;
    public string UnitName { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
}

public sealed class UnitInput
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Olcu birimi kodu zorunludur.")]
    [MaxLength(20, ErrorMessage = "Olcu birimi kodu en fazla 20 karakter olabilir.")]
    public string UnitCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Olcu birimi adi zorunludur.")]
    [MaxLength(100, ErrorMessage = "Olcu birimi adi en fazla 100 karakter olabilir.")]
    public string UnitName { get; set; } = string.Empty;

    [MaxLength(20, ErrorMessage = "Uluslararasi kod en fazla 20 karakter olabilir.")]
    public string? IntlCode { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Siralama 0'dan kucuk olamaz.")]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
