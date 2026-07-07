using System.ComponentModel.DataAnnotations;
using CalibraHub.Web.Models.Shared;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Models.Logistics;

public sealed class LocationsViewModel
{
    public required IReadOnlyCollection<LocationRowViewModel> Locations { get; init; }
    public required IReadOnlyCollection<SelectListItem> LocationTypeOptions { get; init; }
    public required IReadOnlyCollection<SelectListItem> ParentLocationOptions { get; init; }
    public required IReadOnlyCollection<ScreenLayoutTabViewModel> LayoutTabs { get; init; }
    public required GridListStateViewModel ListState { get; init; }
    public LocationInput LocationInput { get; init; } = new();
}

public sealed class LocationRowViewModel
{
    public int Id { get; init; }
    public int? ParentId { get; init; }
    public string? ParentLocationCode { get; init; }
    public string ParentLocationDisplayName { get; init; } = "-";
    public string LocationTypeCode { get; init; } = string.Empty;
    public string LocationTypeDisplayName { get; init; } = string.Empty;
    public string LocationCode { get; init; } = string.Empty;
    public string? LocationName { get; init; }
    public int SortOrder { get; init; }
    public decimal? MaxWeightCapacity { get; init; }
    public decimal? VolumeCapacity { get; init; }
    public bool IsActive { get; init; }
}

public sealed class LocationInput
{
    public int? Id { get; set; }

    public int? ParentId { get; set; }

    [Required(ErrorMessage = "Lokasyon tipi zorunludur.")]
    [MaxLength(20, ErrorMessage = "Lokasyon tipi en fazla 20 karakter olabilir.")]
    public string LocationTypeCode { get; set; } = "SECTION";

    [Required(ErrorMessage = "Lokasyon kodu zorunludur.")]
    [MaxLength(50, ErrorMessage = "Lokasyon kodu en fazla 50 karakter olabilir.")]
    public string LocationCode { get; set; } = string.Empty;

    [MaxLength(100, ErrorMessage = "Lokasyon adi en fazla 100 karakter olabilir.")]
    public string? LocationName { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Siralama 0'dan kucuk olamaz.")]
    public int SortOrder { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999", ErrorMessage = "Maksimum agirlik kapasitesi negatif olamaz.")]
    public decimal? MaxWeightCapacity { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999", ErrorMessage = "Hacim kapasitesi negatif olamaz.")]
    public decimal? VolumeCapacity { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsMachinePark { get; set; }

    public bool IsStorageArea { get; set; }

    // Eksi bakiye izni (üç durumlu): null=devral, true=izin (kontrol kapalı), false=engelle (kontrol açık).
    public bool? AllowNegativeBalance { get; set; }
}
