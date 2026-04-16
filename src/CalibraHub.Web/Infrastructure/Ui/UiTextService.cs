using System.Globalization;
using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Ui;

namespace CalibraHub.Web.Infrastructure.Ui;

public interface IUiTextService
{
    Task<UiRuntimeState> GetRuntimeAsync(CancellationToken cancellationToken = default);
    Task<UiFormTextSet> GetFormAsync(string formKey, CancellationToken cancellationToken = default);
}

public sealed record UiRuntimeState(string LanguageCode, string ThemeCode);

public sealed class UiFormTextSet
{
    private readonly string _formKey;
    private readonly string _languageCode;
    private readonly IReadOnlyDictionary<string, string> _texts;

    public UiFormTextSet(string formKey, string languageCode, IReadOnlyDictionary<string, string> texts)
    {
        _formKey = formKey;
        _languageCode = languageCode;
        _texts = texts;
    }

    public string Text(string labelKey, string? fallback = null) =>
        _texts.GetValueOrDefault(labelKey)
        ?? fallback
        ?? UiCatalog.GetText(_formKey, labelKey, _languageCode);
}

public sealed class UiTextService : IUiTextService
{
    private readonly IUiConfigurationService _uiConfigurationService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private Task? _loadTask;
    private UiRuntimeState _runtime = new(UiCatalog.DefaultLanguageCode, UiCatalog.DefaultThemeCode);
    private Dictionary<string, Dictionary<string, string>> _textsByForm =
        new(StringComparer.OrdinalIgnoreCase);

    public UiTextService(
        IUiConfigurationService uiConfigurationService,
        IHttpContextAccessor httpContextAccessor)
    {
        _uiConfigurationService = uiConfigurationService;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<UiRuntimeState> GetRuntimeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _runtime;
    }

    public async Task<UiFormTextSet> GetFormAsync(string formKey, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        if (!_textsByForm.TryGetValue(formKey, out var formTexts))
        {
            formTexts = BuildCatalogFormTexts(formKey, _runtime.LanguageCode);
            _textsByForm[formKey] = formTexts;
        }

        return new UiFormTextSet(formKey, _runtime.LanguageCode, formTexts);
    }

    private Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        _loadTask ??= LoadAsync(cancellationToken);
        return _loadTask;
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        var preference = await _uiConfigurationService.GetUserPreferenceAsync(userId, cancellationToken);
        _runtime = new UiRuntimeState(preference.LanguageCode, preference.ThemeCode);
        ApplyCulture(_runtime.LanguageCode);

        _textsByForm = UiCatalog.GetForms()
            .ToDictionary(
                form => form.FormKey,
                form => form.Labels.ToDictionary(
                    label => label.Key,
                    label => UiCatalog.GetText(form.FormKey, label.Key, _runtime.LanguageCode),
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        var translations = await _uiConfigurationService.GetTranslationsAsync(_runtime.LanguageCode, cancellationToken);
        foreach (var translation in translations)
        {
            if (!_textsByForm.TryGetValue(translation.FormKey, out var formTexts))
            {
                formTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _textsByForm[translation.FormKey] = formTexts;
            }

            formTexts[translation.LabelKey] = translation.LabelText;
        }
    }

    private Guid? ResolveUserId()
    {
        var rawUserId = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawUserId, out var userId) ? userId : null;
    }

    private static Dictionary<string, string> BuildCatalogFormTexts(string formKey, string languageCode)
    {
        var form = UiCatalog.GetForm(formKey);
        if (form is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return form.Labels.ToDictionary(
            label => label.Key,
            label => UiCatalog.GetText(form.FormKey, label.Key, languageCode),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void ApplyCulture(string languageCode)
    {
        var culture = CultureInfo.GetCultureInfo(languageCode);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
