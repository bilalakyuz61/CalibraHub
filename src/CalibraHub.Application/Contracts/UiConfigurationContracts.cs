namespace CalibraHub.Application.Contracts;

public sealed record UiLanguageOptionDto(string Code, string DisplayName);

public sealed record UiThemeOptionDto(string Code, string DisplayName);

public sealed record UiFormOptionDto(string FormKey, string DisplayName);

public sealed record UserInterfacePreferenceDto(string LanguageCode, string ThemeCode);

public sealed record UiLabelEditorEntryDto(
    string LabelKey,
    string DefaultText,
    string CurrentText,
    string OverrideText);

public sealed record UiLabelTranslationDto(
    string FormKey,
    string LabelKey,
    string LanguageCode,
    string LabelText);

public sealed record SaveUserInterfacePreferenceRequest(
    Guid UserId,
    string LanguageCode,
    string ThemeCode);

public sealed record SaveUiLabelTranslationsRequest(
    string FormKey,
    string LanguageCode,
    IReadOnlyCollection<SaveUiLabelTranslationEntryRequest> Entries);

public sealed record SaveUiLabelTranslationEntryRequest(
    string LabelKey,
    string LabelText);
