using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// AppearanceLabelsController — Appearance/Form Labels JSON endpoint'leri
/// (rapor §2.3 AdminController split).
///
/// Tasinmis endpoint'ler:
///   - GET  /Admin/GetAppearanceFormDataJson    → dil/tema/form listesi + mevcut secim
///   - GET  /Admin/GetFormLabelsJson            → form etiketi liste (override + default)
///   - POST /Admin/SaveFormLabelsJson           → form etiketi cevirilerini kaydet
/// </summary>
[Authorize]
public sealed class AppearanceLabelsController : Controller
{
    private readonly IUiConfigurationService _uiConfigurationService;

    public AppearanceLabelsController(IUiConfigurationService uiConfigurationService)
    {
        _uiConfigurationService = uiConfigurationService;
    }

    private int? GetCurrentUserId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(rawUserId, out var userId) ? userId : null;
    }

    [HttpGet("/Admin/GetAppearanceFormDataJson")]
    public async Task<IActionResult> GetAppearanceFormDataJson(CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        var preference = await _uiConfigurationService.GetUserPreferenceAsync(currentUserId, ct);
        var languages = _uiConfigurationService.GetSupportedLanguages()
            .Select(x => new { x.Code, x.DisplayName }).ToArray();
        var themes = _uiConfigurationService.GetSupportedThemes()
            .Select(x => new { x.Code, x.DisplayName }).ToArray();
        var forms = _uiConfigurationService.GetSupportedForms()
            .Select(x => new { x.FormKey, x.DisplayName }).ToArray();
        return Json(new
        {
            languages, themes, forms,
            currentLanguage = preference.LanguageCode,
            currentTheme = preference.ThemeCode
        });
    }

    [HttpGet("/Admin/GetFormLabelsJson")]
    public async Task<IActionResult> GetFormLabelsJson(string formKey, string languageCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formKey) || string.IsNullOrWhiteSpace(languageCode))
            return Json(Array.Empty<object>());
        var entries = await _uiConfigurationService.GetLabelEditorEntriesAsync(formKey, languageCode, ct);
        return Json(entries.Select(x => new
        {
            x.LabelKey, x.DefaultText, x.CurrentText, overrideText = x.OverrideText ?? string.Empty
        }).ToArray());
    }

    [HttpPost("/Admin/SaveFormLabelsJson")]
    public async Task<IActionResult> SaveFormLabelsJson([FromBody] SaveFormLabelsJsonInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.FormKey))
            return Json(new { success = false, message = "Form secilmedi." });
        try
        {
            await _uiConfigurationService.SaveLabelTranslationsAsync(
                new SaveUiLabelTranslationsRequest(
                    input.FormKey, input.LanguageCode,
                    (input.Labels ?? []).Select(x => new SaveUiLabelTranslationEntryRequest(x.Key, x.Text)).ToArray()),
                ct);
            return Json(new { success = true, message = "Form etiketleri kaydedildi." });
        }
        catch (ArgumentException ex) { return Json(new { success = false, message = ex.Message }); }
    }
}
