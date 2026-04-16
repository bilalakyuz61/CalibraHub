using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Models.Admin;

public sealed class AppearanceSettingsViewModel
{
    [Required]
    public string LanguageCode { get; set; } = "tr-TR";

    [Required]
    public string ThemeCode { get; set; } = "light";

    [Required]
    public string SelectedFormKey { get; set; } = "home.dashboard";

    [Required]
    public string SelectedLanguageCode { get; set; } = "tr-TR";

    public IReadOnlyCollection<SelectListItem> PreferenceLanguageOptions { get; init; } = Array.Empty<SelectListItem>();
    public IReadOnlyCollection<SelectListItem> EditorLanguageOptions { get; init; } = Array.Empty<SelectListItem>();
    public IReadOnlyCollection<SelectListItem> ThemeOptions { get; init; } = Array.Empty<SelectListItem>();
    public IReadOnlyCollection<SelectListItem> FormOptions { get; init; } = Array.Empty<SelectListItem>();
    public List<UiLabelEditorInputModel> Labels { get; init; } = [];
}

public sealed class UiLabelEditorInputModel
{
    [Required]
    public string LabelKey { get; set; } = string.Empty;

    public string DefaultText { get; set; } = string.Empty;

    public string CurrentText { get; set; } = string.Empty;

    public string LabelText { get; set; } = string.Empty;
}
