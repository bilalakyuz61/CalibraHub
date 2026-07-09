using System.ComponentModel.DataAnnotations;
using CalibraHub.Web.Models.Shared;

namespace CalibraHub.Web.Models.Logistics;

public sealed class MaterialCardsViewModel
{
    public required IReadOnlyCollection<MaterialCardRowViewModel> MaterialCards { get; init; }
    public required IReadOnlyCollection<MaterialCardLookupViewModel> MaterialCardLookup { get; init; }
    public required MaterialCardListStateViewModel ListState { get; init; }

    public MaterialCardCreateInput StockInput { get; init; } = new();
    public MaterialCardMetaViewModel? SelectedMeta { get; init; }
    public IReadOnlyCollection<MaterialCardCombinationViewModel>? Combinations { get; init; }

    // Olcu birimi secenekleri
    public IReadOnlyCollection<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> MeasureUnits { get; init; } = [];

    // Cari hesaplar (tedarikci secimi)
    public IReadOnlyCollection<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> SupplierAccounts { get; init; } = [];

    // Grid kolon tercihleri
    public IReadOnlyCollection<GridColumnDefinition> AvailableColumns { get; init; } = [];
    public IReadOnlyCollection<string> VisibleColumns { get; init; } = [];

    // CalibraSmartBoard inline config — anonymous object (JSON serialize edilecek)
    public object? BoardConfig { get; init; }
}

public sealed class GridColumnDefinition
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class MaterialCardCombinationViewModel
{
    public int Id { get; init; }
    public string CombinationCode { get; init; } = string.Empty;
    public string CombinationName { get; init; } = string.Empty;
}

public sealed class MaterialCardMetaViewModel
{
    public int? CreatedByUserId { get; init; }
    public DateTime? CreatedDate { get; init; }
    public int? ModifiedByUserId { get; init; }
    public DateTime? ModifiedDate { get; init; }
}

public sealed class MaterialCardListStateViewModel : GridListStateViewModel
{
}

public static class MaterialCardListSortOptions
{
    public const string Code = "code";
    public const string Name = "name";
}

public sealed class MaterialCardRowViewModel
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int? TypeId { get; init; }
    public int? UnitId { get; init; }
    public string? UnitName { get; init; }
    public bool IsActive { get; init; }
}

public sealed class MaterialCardLookupViewModel
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string EditUrl { get; init; } = string.Empty;
}

public sealed class MaterialCardCreateInput
{
    public int? ItemId { get; set; }

    [Required(ErrorMessage = "Malzeme kodu zorunludur.")]
    [MaxLength(50, ErrorMessage = "Malzeme kodu en fazla 50 karakter olabilir.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Malzeme adi zorunludur.")]
    [MaxLength(200, ErrorMessage = "Malzeme adi en fazla 200 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    public int? TypeId { get; set; }

    public int? UnitId { get; set; }

    public bool Combinations { get; set; }
}

public sealed class SaveMaterialCardJsonInput
{
    public int? ItemId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? TypeId { get; set; }
    public int? UnitId { get; set; }
    public bool Combinations { get; set; }
    public decimal TaxRate { get; set; } = 20m;
    public string? TrackingType { get; set; } = "None";
    /// <summary>Planlama: genel asgari stok (ana birim).</summary>
    public decimal? MinStock { get; set; }
    /// <summary>Seri takibinde giriş serisi otomatik üretilsin (yalnız TrackingType='Serial').</summary>
    public bool AutoSerial { get; set; }
}
