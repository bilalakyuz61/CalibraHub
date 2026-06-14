using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IUiConfigurationService
{
    IReadOnlyCollection<UiLanguageOptionDto> GetSupportedLanguages();
    IReadOnlyCollection<UiThemeOptionDto> GetSupportedThemes();
    IReadOnlyCollection<UiFormOptionDto> GetSupportedForms();
    Task<UserInterfacePreferenceDto> GetUserPreferenceAsync(int? userId, CancellationToken cancellationToken);
    Task<int> GetGridPageSizePreferenceAsync(int? userId, string gridKey, int defaultPageSize, CancellationToken cancellationToken);
    Task SaveUserPreferenceAsync(SaveUserInterfacePreferenceRequest request, CancellationToken cancellationToken);
    Task SaveGridPageSizePreferenceAsync(int userId, string gridKey, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<UiLabelEditorEntryDto>> GetLabelEditorEntriesAsync(
        string formKey,
        string languageCode,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<UiLabelTranslationDto>> GetTranslationsAsync(
        string languageCode,
        CancellationToken cancellationToken);
    Task SaveLabelTranslationsAsync(SaveUiLabelTranslationsRequest request, CancellationToken cancellationToken);
    IReadOnlyCollection<ScreenDesignScreenDto> GetSupportedScreenDesigns();
    Task<ScreenDesignLayoutDto> GetScreenDesignLayoutAsync(string screenCode, CancellationToken cancellationToken);
    Task SaveScreenDesignLayoutAsync(SaveScreenDesignLayoutRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<string>> GetGridColumnPreferencesAsync(int? userId, string gridKey, CancellationToken cancellationToken);
    Task SaveGridColumnPreferencesAsync(int userId, string gridKey, IReadOnlyCollection<string> columns, CancellationToken cancellationToken);
}
