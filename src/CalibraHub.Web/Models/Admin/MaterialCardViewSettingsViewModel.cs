using CalibraHub.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace CalibraHub.Web.Models.Admin;

public sealed class MaterialCardViewSettingsViewModel
{
    public List<FieldGroupListItemViewModel> Groups { get; set; } = new();
    public List<MaterialCardFieldListItemViewModel> Fields { get; set; } = new();
    public FieldGroupInput GroupInput { get; set; } = new();
    public MaterialCardDynamicFieldInput FieldInput { get; set; } = new();
    public IReadOnlyCollection<SelectListItem> GroupOptions { get; init; } = Array.Empty<SelectListItem>();
    public IReadOnlyCollection<SelectListItem> DataTypeOptions { get; init; } = Array.Empty<SelectListItem>();
}

public sealed class FieldGroupInput
{
    public Guid? GroupId { get; set; }

    [Required(ErrorMessage = "Grup teknik adi zorunludur.")]
    public string GroupKey { get; set; } = string.Empty;

    [Required(ErrorMessage = "Grup etiketi zorunludur.")]
    public string GroupLabel { get; set; } = string.Empty;

    [Range(0, int.MaxValue, ErrorMessage = "Siralama negatif olamaz.")]
    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Hangi ekran (modul) icin grup — React/form tarafindan gelir.</summary>
    public string? ScreenCode { get; set; }

    /// <summary>Multi-layer ekranlar icin katman (ornek: sales_quotes icin "header"/"line"). Bos veya "default" → tek katman.</summary>
    public string? LayerKey { get; set; }
}

public sealed class MaterialCardDynamicFieldInput
{
    public Guid? FieldId { get; set; }
    public Guid? GroupId { get; set; }

    /// <summary>Hangi ekran (modul) icin field — React/form tarafindan gelir.</summary>
    public string? ScreenCode { get; set; }

    /// <summary>Multi-layer ekranlar icin katman (ornek: sales_quotes icin "header"/"line"). Bos veya "default" → tek katman.</summary>
    public string? LayerKey { get; set; }

    [Required(ErrorMessage = "Veritabani saha adi zorunludur.")]
    public string FieldKey { get; set; } = string.Empty;

    [Required(ErrorMessage = "Gorunecek ad zorunludur.")]
    public string FieldLabel { get; set; } = string.Empty;

    [Required(ErrorMessage = "Veri tipi zorunludur.")]
    public string DataType { get; set; } = MaterialCardDynamicFieldDataType.String.ToString();

    public bool IsVisible { get; set; } = true;
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Siralama negatif olamaz.")]
    public int DisplayOrder { get; set; }

    [Range(1, 3, ErrorMessage = "Alan genisligi 1 ile 3 arasinda olmalidir.")]
    public int ColumnSpan { get; set; } = 1;

    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }
    public List<MaterialCardFieldOptionInput> Options { get; set; } = new();
}

public sealed class MaterialCardFieldOptionInput
{
    public Guid? OptionId { get; set; }

    [Required(ErrorMessage = "Secenek anahtari zorunludur.")]
    public string OptionKey { get; set; } = string.Empty;

    [Required(ErrorMessage = "Secenek metni zorunludur.")]
    public string OptionLabel { get; set; } = string.Empty;

    [Range(0, int.MaxValue, ErrorMessage = "Siralama negatif olamaz.")]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class FieldGroupListItemViewModel
{
    public Guid Id { get; init; }
    public string GroupKey { get; init; } = string.Empty;
    public string GroupLabel { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }
    public bool IsActive { get; init; }
    /// <summary>Multi-layer ekranlar icin katman (orn. sales_quotes: "header"/"line"). Null → tek katman.</summary>
    public string? LayerKey { get; init; }
}

public sealed class MaterialCardFieldListItemViewModel
{
    public Guid Id { get; init; }
    public Guid? GroupId { get; init; }
    public string GroupLabel { get; init; } = "-";
    public string FieldKey { get; init; } = string.Empty;
    public string FieldLabel { get; init; } = string.Empty;
    public MaterialCardDynamicFieldDataType DataType { get; init; }
    public bool IsVisible { get; init; }
    public bool IsRequired { get; init; }
    public string? DefaultValue { get; init; }
    public int DisplayOrder { get; init; }
    public int ColumnSpan { get; init; } = 1;
    public bool IsSystem { get; init; }
    public bool IsActive { get; init; }
    /// <summary>Multi-layer ekranlar icin katman. Null → tek katman.</summary>
    public string? LayerKey { get; init; }
    public IReadOnlyCollection<MaterialCardFieldOptionListItemViewModel> Options { get; init; } = Array.Empty<MaterialCardFieldOptionListItemViewModel>();
}

public sealed class MaterialCardFieldOptionListItemViewModel
{
    public Guid Id { get; init; }
    public string OptionKey { get; init; } = string.Empty;
    public string OptionLabel { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
}
