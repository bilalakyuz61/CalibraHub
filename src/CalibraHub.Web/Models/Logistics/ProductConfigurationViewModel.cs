using System.ComponentModel.DataAnnotations;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Shared;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Models.Logistics;

public sealed class ProductConfigurationViewModel
{
    // SmartBoard config (server-side hazirlandi — BuildProductConfigurationBoardConfigAsync).
    // View inline JSON olarak mountSmartBoard'a gecer.
    public object? BoardConfig { get; init; }

    // Geriye uyum — eski SaveProductFeature / SaveProductValue / SaveProductConfiguration
    // action'lari hala bu field'lari doldurmak zorunda (cunku ayni View'i render ediyorlardi).
    // Yeni akista ProductConfiguration action'i sadece BoardConfig doldurur.
    public IReadOnlyCollection<ProductConfigurationFeatureDto> Features { get; init; } = Array.Empty<ProductConfigurationFeatureDto>();
    public IReadOnlyCollection<ProductConfigurationValueDto> Values { get; init; } = Array.Empty<ProductConfigurationValueDto>();
    public IReadOnlyCollection<ProductConfigurationItemDto> Configurations { get; init; } = Array.Empty<ProductConfigurationItemDto>();
    public IReadOnlyCollection<ProductConfigurationFeatureDto> GridFeatures { get; init; } = Array.Empty<ProductConfigurationFeatureDto>();
    public IReadOnlyCollection<ProductConfigurationValueDto> GridValues { get; init; } = Array.Empty<ProductConfigurationValueDto>();
    public IReadOnlyCollection<ProductConfigurationItemDto> GridConfigurations { get; init; } = Array.Empty<ProductConfigurationItemDto>();
    public IReadOnlyCollection<SelectListItem> DataTypeOptions { get; init; } = Array.Empty<SelectListItem>();
    public IReadOnlyCollection<SelectListItem> UnitOfMeasureOptions { get; init; } = Array.Empty<SelectListItem>();
    public IReadOnlyCollection<SelectListItem> FeatureOptions { get; init; } = Array.Empty<SelectListItem>();
    public IReadOnlyCollection<SelectListItem> ValueOptions { get; init; } = Array.Empty<SelectListItem>();
    public IReadOnlyCollection<SelectListItem> MaterialCodeOptions { get; init; } = Array.Empty<SelectListItem>();
    public IReadOnlyCollection<ProductFeatureStockOptionViewModel> FeatureStockOptions { get; init; } = Array.Empty<ProductFeatureStockOptionViewModel>();
    public GridListStateViewModel FeaturesListState { get; init; } = new();
    public GridListStateViewModel ValuesListState { get; init; } = new();
    public GridListStateViewModel ConfigurationsListState { get; init; } = new();
    public string ActiveTab { get; init; } = ProductConfigurationTabs.Feature;
    public int? SelectedFeatureId { get; init; }
    public int? SelectedValueId { get; init; }
    public int? SelectedConfigId { get; init; }
    public ProductFeatureInput FeatureInput { get; init; } = new();
    public ProductValueInput ValueInput { get; init; } = new();
    public ProductConfigInput ConfigInput { get; init; } = new();
    public IReadOnlyCollection<ProductFeatureRowViewModel> FeatureRows { get; init; } = Array.Empty<ProductFeatureRowViewModel>();
}

/// <summary>
/// Yeni ProductFeatureEdit sayfasi icin minimal ViewModel.
/// View bundle'i mount ederken id'yi okuyup detay fetch eder.
/// </summary>
public sealed class ProductFeatureEditViewModel
{
    public int? FeatureId { get; set; }
}

public static class ProductConfigurationTabs
{
    public const string Feature = "feature";
    public const string Value = "value";
    public const string Config = "config";
}

public sealed class ProductFeatureInput
{
    [Required(ErrorMessage = "Ozellik adi zorunludur.")]
    [MaxLength(255, ErrorMessage = "Ozellik adi en fazla 255 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veri tipi secimi zorunludur.")]
    public string DataType { get; set; } = string.Empty;

    public string? UnitOfMeasure { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class FeatureEditInput
{
    [Required]
    public int Id { get; set; }

    [Required(ErrorMessage = "Ozellik adi zorunludur.")]
    [MaxLength(255, ErrorMessage = "Ozellik adi en fazla 255 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veri tipi secimi zorunludur.")]
    public string DataType { get; set; } = string.Empty;

    public string? UnitOfMeasure { get; set; }
}

public sealed class ProductValueInput
{
    [Required(ErrorMessage = "Ozellik secimi zorunludur.")]
    public int? FeatureId { get; set; }

    [MaxLength(120, ErrorMessage = "Deger aciklamasi en fazla 120 karakter olabilir.")]
    public string? Description { get; set; }

    [MaxLength(100, ErrorMessage = "Metin degeri en fazla 100 karakter olabilir.")]
    public string? TextValue { get; set; }

    [Range(typeof(decimal), "-999999999999999", "999999999999999", ErrorMessage = "Sayisal deger gecersiz.")]
    public decimal? NumericValue { get; set; }

    [DataType(DataType.Date)]
    public DateTime? DateValue { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class ProductConfigInput
{
    [Required(ErrorMessage = "Malzeme kodu secimi zorunludur.")]
    public string RelatedMaterialCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ozellik secimi zorunludur.")]
    public int? FeatureId { get; set; }

    [Required(ErrorMessage = "Deger secimi zorunludur.")]
    public int? ValueId { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class ProductFeatureStockOptionViewModel
{
    public required string StockCode { get; init; }
    public required string StockName { get; init; }
    public bool IsSelected { get; init; }
}

public sealed class ProductFeatureRowViewModel
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public string? UnitOfMeasure { get; init; }
    public bool IsActive { get; init; }
    public int ValueCount { get; init; }
    public IReadOnlyList<string> LinkedStockCodes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<FeatureValueItemViewModel> Values { get; init; } = Array.Empty<FeatureValueItemViewModel>();
}

public sealed class FeatureValueItemViewModel
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
