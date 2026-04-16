using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Models.Admin;

public sealed class ScreenDesignSettingsPageViewModel
{
    public string SelectedScreenCode { get; set; } = string.Empty;
    public string SelectedScreenLabel { get; set; } = string.Empty;
    public bool UsesMaterialCardSchema { get; set; }
    public IReadOnlyCollection<SelectListItem> ScreenOptions { get; set; } = Array.Empty<SelectListItem>();
    public MaterialCardViewSettingsViewModel MaterialCardDesigner { get; set; } = new();
    public StandardScreenDesignViewModel StandardDesigner { get; set; } = new();
}

public sealed class StandardScreenDesignViewModel
{
    public string ScreenCode { get; set; } = string.Empty;
    public string ScreenLabel { get; set; } = string.Empty;
    public List<StandardScreenDesignTabInput> Tabs { get; set; } = new();
    public List<StandardScreenDesignItemInput> Items { get; set; } = new();
}

public sealed class StandardScreenDesignTabInput
{
    public string TabKey { get; set; } = string.Empty;
    public string TabLabel { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class StandardScreenDesignItemInput
{
    public string ItemKey { get; set; } = string.Empty;
    public string ItemLabel { get; set; } = string.Empty;
    public string TabKey { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public int ColumnSpan { get; set; } = 1;
    public bool IsVisible { get; set; } = true;
    public bool IsRequired { get; set; }
}
