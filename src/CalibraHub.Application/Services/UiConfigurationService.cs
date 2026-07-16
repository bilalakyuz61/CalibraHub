using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Ui;
using CalibraHub.Domain.Entities;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CalibraHub.Application.Services;

public sealed class UiConfigurationService : IUiConfigurationService
{
    private static readonly Regex ScreenDesignTabKeyRegex = new("^[a-z][a-z0-9_]{1,39}$", RegexOptions.Compiled);
    private static readonly int[] SupportedGridPageSizes = [10, 20, 30, 50, 100];
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly IUiLabelTranslationRepository _uiLabelTranslationRepository;
    private readonly IScreenLayoutRepository _screenLayoutRepository;
    private readonly IUserSettingRepository _userSettingRepository;

    public UiConfigurationService(
        IUserProfileRepository userProfileRepository,
        IUiLabelTranslationRepository uiLabelTranslationRepository,
        IScreenLayoutRepository screenLayoutRepository,
        IUserSettingRepository userSettingRepository)
    {
        _userProfileRepository = userProfileRepository;
        _uiLabelTranslationRepository = uiLabelTranslationRepository;
        _screenLayoutRepository = screenLayoutRepository;
        _userSettingRepository = userSettingRepository;
    }

    public IReadOnlyCollection<UiLanguageOptionDto> GetSupportedLanguages() =>
        UiCatalog.GetLanguages()
            .Select(x => new UiLanguageOptionDto(x.Code, x.DisplayName))
            .ToArray();

    public IReadOnlyCollection<UiThemeOptionDto> GetSupportedThemes() =>
        UiCatalog.GetThemes()
            .Select(x => new UiThemeOptionDto(x.Code, x.DisplayName))
            .ToArray();

    public IReadOnlyCollection<UiFormOptionDto> GetSupportedForms() =>
        UiCatalog.GetForms()
            .Select(x => new UiFormOptionDto(x.FormKey, x.DisplayName))
            .ToArray();

    public async Task<UserInterfacePreferenceDto> GetUserPreferenceAsync(
        int? userId,
        CancellationToken cancellationToken)
    {
        if (!userId.HasValue || userId.Value <= 0)
        {
            return new UserInterfacePreferenceDto(UiCatalog.DefaultLanguageCode, UiCatalog.DefaultThemeCode);
        }

        var user = await _userProfileRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null)
        {
            return new UserInterfacePreferenceDto(UiCatalog.DefaultLanguageCode, UiCatalog.DefaultThemeCode);
        }

        return new UserInterfacePreferenceDto(
            UiCatalog.NormalizeLanguageCode(user.LanguageCode),
            UiCatalog.NormalizeThemeCode(user.ThemeCode));
    }

    public async Task<int> GetGridPageSizePreferenceAsync(
        int? userId,
        string gridKey,
        int defaultPageSize,
        CancellationToken cancellationToken)
    {
        var normalizedDefaultPageSize = NormalizeGridPageSize(defaultPageSize);
        var normalizedGridKey = NormalizeGridPreferenceKey(gridKey);
        if (string.IsNullOrEmpty(normalizedGridKey) || !userId.HasValue || userId.Value <= 0)
        {
            return normalizedDefaultPageSize;
        }

        var user = await _userProfileRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null)
        {
            return normalizedDefaultPageSize;
        }

        var preferences = DeserializeGridPreferences(user.GridPreferencesJson);
        return preferences.TryGetValue(normalizedGridKey, out var pageSize)
            ? NormalizeGridPageSize(pageSize)
            : normalizedDefaultPageSize;
    }

    public async Task SaveUserPreferenceAsync(
        SaveUserInterfacePreferenceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.UserId <= 0)
        {
            throw new ArgumentException("Kullanici bilgisi bulunamadi.");
        }

        var user = await _userProfileRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new ArgumentException("Kullanici bulunamadi.");
        }

        var normalizedLanguageCode = UiCatalog.NormalizeLanguageCode(request.LanguageCode);
        var normalizedThemeCode = UiCatalog.NormalizeThemeCode(request.ThemeCode);

        user.SetInterfacePreferences(normalizedLanguageCode, normalizedThemeCode);
        await _userProfileRepository.UpdateAsync(user, cancellationToken);
    }

    public async Task SaveGridPageSizePreferenceAsync(
        int userId,
        string gridKey,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (userId <= 0)
        {
            throw new ArgumentException("Kullanici bilgisi bulunamadi.");
        }

        var normalizedGridKey = NormalizeGridPreferenceKey(gridKey);
        if (string.IsNullOrEmpty(normalizedGridKey))
        {
            throw new ArgumentException("Grid anahtari gecersiz.");
        }

        var user = await _userProfileRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new ArgumentException("Kullanici bulunamadi.");
        }

        var preferences = DeserializeGridPreferences(user.GridPreferencesJson);
        preferences[normalizedGridKey] = NormalizeGridPageSize(pageSize);
        user.SetGridPreferencesJson(JsonSerializer.Serialize(preferences));
        await _userProfileRepository.UpdateAsync(user, cancellationToken);
    }

    public async Task<IReadOnlyCollection<UiLabelEditorEntryDto>> GetLabelEditorEntriesAsync(
        string formKey,
        string languageCode,
        CancellationToken cancellationToken)
    {
        var form = UiCatalog.GetForm(formKey)
                   ?? throw new ArgumentException("Secilen form katalogda bulunamadi.");
        var normalizedLanguageCode = UiCatalog.NormalizeLanguageCode(languageCode);
        var overrides = await _uiLabelTranslationRepository.GetByFormAndLanguageAsync(
            form.FormKey,
            normalizedLanguageCode,
            cancellationToken);
        var overrideLookup = overrides.ToDictionary(x => x.LabelKey, x => x.LabelText, StringComparer.OrdinalIgnoreCase);

        return form.Labels
            .Select(label =>
            {
                var defaultText = UiCatalog.GetText(form.FormKey, label.Key, normalizedLanguageCode);
                var overrideText = overrideLookup.GetValueOrDefault(label.Key, string.Empty);
                var currentText = string.IsNullOrWhiteSpace(overrideText) ? defaultText : overrideText;

                return new UiLabelEditorEntryDto(
                    label.Key,
                    defaultText,
                    currentText,
                    overrideText);
            })
            .ToArray();
    }

    public async Task<IReadOnlyCollection<UiLabelTranslationDto>> GetTranslationsAsync(
        string languageCode,
        CancellationToken cancellationToken)
    {
        var normalizedLanguageCode = UiCatalog.NormalizeLanguageCode(languageCode);
        var items = await _uiLabelTranslationRepository.GetByLanguageAsync(normalizedLanguageCode, cancellationToken);

        return items
            .Select(x => new UiLabelTranslationDto(x.FormKey, x.LabelKey, x.LanguageCode, x.LabelText))
            .ToArray();
    }

    public async Task SaveLabelTranslationsAsync(
        SaveUiLabelTranslationsRequest request,
        CancellationToken cancellationToken)
    {
        var form = UiCatalog.GetForm(request.FormKey)
                   ?? throw new ArgumentException("Secilen form katalogda bulunamadi.");
        var normalizedLanguageCode = UiCatalog.NormalizeLanguageCode(request.LanguageCode);

        var entries = request.Entries ?? Array.Empty<SaveUiLabelTranslationEntryRequest>();
        var catalogKeys = form.Labels
            .Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (entries.Any(x => !catalogKeys.Contains(x.LabelKey)))
        {
            throw new ArgumentException("Forma ait olmayan etiket anahtari gonderildi.");
        }

        var translations = entries
            .Select(entry =>
            {
                var defaultText = UiCatalog.GetText(form.FormKey, entry.LabelKey, normalizedLanguageCode);
                var normalizedText = entry.LabelText?.Trim() ?? string.Empty;

                return new
                {
                    entry.LabelKey,
                    DefaultText = defaultText,
                    LabelText = normalizedText
                };
            })
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.LabelText) &&
                !string.Equals(x.LabelText, x.DefaultText, StringComparison.Ordinal))
            .Select(x => new UiLabelTranslation
            {
                Id = Guid.NewGuid(),
                FormKey = form.FormKey,
                LabelKey = x.LabelKey,
                LanguageCode = normalizedLanguageCode,
                LabelText = x.LabelText,
                Updated = DateTime.Now
            })
            .ToArray();

        await _uiLabelTranslationRepository.ReplaceFormLanguageAsync(
            form.FormKey,
            normalizedLanguageCode,
            translations,
            cancellationToken);
    }

    public IReadOnlyCollection<ScreenDesignScreenDto> GetSupportedScreenDesigns() =>
        ScreenDesignCatalog.GetSupportedScreens();

    public async Task<ScreenDesignLayoutDto> GetScreenDesignLayoutAsync(
        string screenCode,
        CancellationToken cancellationToken)
    {
        var normalizedScreenCode = ScreenDesignCatalog.NormalizeScreenCode(screenCode);
        if (ScreenDesignCatalog.UsesMaterialCardSchema(normalizedScreenCode))
        {
            return ScreenDesignCatalog.GetDefaultLayout(normalizedScreenCode);
        }

        var defaults = ScreenDesignCatalog.GetDefaultLayout(normalizedScreenCode);
        var persisted = await _screenLayoutRepository.GetByScreenCodeAsync(normalizedScreenCode, cancellationToken);
        if (persisted is null || string.IsNullOrWhiteSpace(persisted.LayoutJson))
        {
            return defaults;
        }

        try
        {
            var document = JsonSerializer.Deserialize<ScreenDesignLayoutDocument>(persisted.LayoutJson) ?? new ScreenDesignLayoutDocument();
            return MergeScreenLayout(defaults, document);
        }
        catch (JsonException)
        {
            return defaults;
        }
    }

    public async Task SaveScreenDesignLayoutAsync(
        SaveScreenDesignLayoutRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedScreenCode = ScreenDesignCatalog.NormalizeScreenCode(request.ScreenCode);
        if (ScreenDesignCatalog.UsesMaterialCardSchema(normalizedScreenCode))
        {
            throw new ArgumentException("Malzeme karti tasarimi bu ekranda grup ve saha bazli yonetilir.");
        }

        var availableItems = ScreenDesignCatalog.GetFieldDefinitions(normalizedScreenCode)
            .ToDictionary(x => x.ItemKey, StringComparer.OrdinalIgnoreCase);

        if (availableItems.Count == 0)
        {
            throw new ArgumentException("Secilen ekran icin tasarim katalogu bulunamadi.");
        }

        var tabs = (request.Tabs ?? Array.Empty<SaveScreenDesignTabRequest>())
            .Select((tab, index) => new ScreenDesignTabDocument(
                NormalizeScreenDesignTabKey(tab.TabKey, index),
                NormalizeScreenDesignText(tab.TabLabel, 120, "Sekme etiketi"),
                Math.Max(0, tab.DisplayOrder),
                tab.IsActive))
            .GroupBy(x => x.TabKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.TabLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (tabs.Length == 0)
        {
            throw new ArgumentException("En az bir aktif sekme tanimlanmalidir.");
        }

        var tabKeyLookup = tabs.ToDictionary(x => x.TabKey, StringComparer.OrdinalIgnoreCase);
        var items = (request.Items ?? Array.Empty<SaveScreenDesignItemRequest>())
            .Select(item =>
            {
                if (!availableItems.ContainsKey(item.ItemKey))
                {
                    throw new ArgumentException("Ekrana ait olmayan saha tasarima gonderildi.");
                }

                var tabKey = NormalizeRequiredTabReference(item.TabKey, tabKeyLookup);
                return new ScreenDesignItemDocument(
                    item.ItemKey.Trim(),
                    tabKey,
                    Math.Max(0, item.DisplayOrder),
                    Math.Clamp(item.ColumnSpan, 1, 3),
                    item.IsVisible,
                    item.IsRequired);
            })
            .GroupBy(x => x.ItemKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.ItemKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (items.Length == 0)
        {
            throw new ArgumentException("Ekrandaki alanlar icin tasarim bilgisi bulunamadi.");
        }

        var document = new ScreenDesignLayoutDocument
        {
            Tabs = tabs.ToList(),
            Items = items.ToList()
        };

        var json = JsonSerializer.Serialize(document);
        var existing = await _screenLayoutRepository.GetByScreenCodeAsync(normalizedScreenCode, cancellationToken);
        var entity = existing ?? new ScreenLayoutDefinition
        {
            Id = Guid.NewGuid(),
            ScreenCode = normalizedScreenCode,
            LayoutJson = json,
            Created = DateTime.Now
        };

        entity.UpdateLayout(json);
        await _screenLayoutRepository.UpsertAsync(entity, cancellationToken);
    }

    private static ScreenDesignLayoutDto MergeScreenLayout(
        ScreenDesignLayoutDto defaults,
        ScreenDesignLayoutDocument document)
    {
        var availableItems = defaults.AvailableItems
            .ToDictionary(x => x.ItemKey, StringComparer.OrdinalIgnoreCase);
        var defaultTabs = defaults.Tabs
            .ToDictionary(x => x.TabKey, StringComparer.OrdinalIgnoreCase);
        var tabs = (document.Tabs ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.TabKey) && !string.IsNullOrWhiteSpace(x.TabLabel))
            .Select(x => new ScreenDesignTabDto(
                x.TabKey.Trim(),
                x.TabLabel.Trim(),
                Math.Max(0, x.DisplayOrder),
                x.IsActive))
            .GroupBy(x => x.TabKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.TabLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tabs.Count == 0)
        {
            tabs = defaults.Tabs.OrderBy(x => x.DisplayOrder).ToList();
        }

        var tabKeys = tabs.Select(x => x.TabKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mergedItems = new List<ScreenDesignItemDto>();

        foreach (var defaultItem in defaults.Items)
        {
            var persistedItem = (document.Items ?? [])
                .FirstOrDefault(x => string.Equals(x.ItemKey, defaultItem.ItemKey, StringComparison.OrdinalIgnoreCase));
            var resolvedTabKey = persistedItem?.TabKey;
            if (string.IsNullOrWhiteSpace(resolvedTabKey) || !tabKeys.Contains(resolvedTabKey))
            {
                resolvedTabKey = defaultItem.TabKey;
            }

            if (!tabKeys.Contains(resolvedTabKey))
            {
                resolvedTabKey = tabs[0].TabKey;
            }

            var label = availableItems.GetValueOrDefault(defaultItem.ItemKey)?.ItemLabel ?? defaultItem.ItemLabel;
            mergedItems.Add(new ScreenDesignItemDto(
                defaultItem.ItemKey,
                label,
                resolvedTabKey,
                persistedItem?.DisplayOrder ?? defaultItem.DisplayOrder,
                Math.Clamp(persistedItem?.ColumnSpan ?? defaultItem.ColumnSpan, 1, 3),
                persistedItem?.IsVisible ?? defaultItem.IsVisible,
                persistedItem?.IsRequired ?? defaultItem.IsRequired));
        }

        return new ScreenDesignLayoutDto(
            defaults.ScreenCode,
            defaults.ScreenLabel,
            tabs,
            mergedItems
                .OrderBy(x => x.TabKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.DisplayOrder)
                .ThenBy(x => x.ItemLabel, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            defaults.AvailableItems);
    }

    private static string NormalizeScreenDesignTabKey(string? value, int index)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = $"tab_{index + 1}";
        }

        normalized = normalized.Replace(' ', '_');
        if (!ScreenDesignTabKeyRegex.IsMatch(normalized))
        {
            throw new ArgumentException("Sekme teknik adi yalnizca kucuk harf, rakam ve alt cizgi icerebilir.");
        }

        return normalized;
    }

    private static string NormalizeScreenDesignText(string? value, int maxLength, string fieldName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{fieldName} zorunludur.");
        }

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string NormalizeRequiredTabReference(
        string? value,
        IReadOnlyDictionary<string, ScreenDesignTabDocument> tabs)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized) || !tabs.ContainsKey(normalized))
        {
            throw new ArgumentException("Her alan gecerli bir sekmeye baglanmalidir.");
        }

        return normalized;
    }

    private static string NormalizeGridPreferenceKey(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static int NormalizeGridPageSize(int value)
    {
        if (SupportedGridPageSizes.Contains(value))
        {
            return value;
        }

        return SupportedGridPageSizes.Contains(20) ? 20 : SupportedGridPageSizes[0];
    }

    private static Dictionary<string, int> DeserializeGridPreferences(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var document = JsonSerializer.Deserialize<Dictionary<string, int>>(rawJson);
            if (document is null)
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }

            return document
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .ToDictionary(
                    x => NormalizeGridPreferenceKey(x.Key),
                    x => NormalizeGridPageSize(x.Value),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class ScreenDesignLayoutDocument
    {
        public List<ScreenDesignTabDocument> Tabs { get; set; } = [];
        public List<ScreenDesignItemDocument> Items { get; set; } = [];
    }

    private sealed record ScreenDesignTabDocument(
        string TabKey,
        string TabLabel,
        int DisplayOrder,
        bool IsActive);

    private sealed record ScreenDesignItemDocument(
        string ItemKey,
        string TabKey,
        int DisplayOrder,
        int ColumnSpan,
        bool IsVisible,
        bool IsRequired);

    // ── Grid kolon tercihleri (user_settings tablosu) ────────────────────

    public async Task<IReadOnlyCollection<string>> GetGridColumnPreferencesAsync(
        int? userId, string gridKey, CancellationToken cancellationToken)
    {
        if (!userId.HasValue || userId.Value <= 0) return [];
        var key = $"grid.columns.{NormalizeGridPreferenceKey(gridKey)}";
        var value = await _userSettingRepository.GetAsync(userId.Value, key, cancellationToken);
        if (string.IsNullOrWhiteSpace(value)) return [];
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task SaveGridColumnPreferencesAsync(
        int userId, string gridKey, IReadOnlyCollection<string> columns, CancellationToken cancellationToken)
    {
        if (userId <= 0) return;
        var key = $"grid.columns.{NormalizeGridPreferenceKey(gridKey)}";
        var value = string.Join(",", columns);
        await _userSettingRepository.SetAsync(userId, key, value, cancellationToken);
    }

    // ── Kısa yol çubuğu (shortcut-bar) tercihi (user_settings tablosu) ───

    public async Task<string?> GetShellShortcutsAsync(int userId, CancellationToken cancellationToken)
    {
        if (userId <= 0) return null;
        return await _userSettingRepository.GetAsync(userId, "ui.shell.shortcuts", cancellationToken);
    }

    public async Task SaveShellShortcutsAsync(int userId, string configJson, CancellationToken cancellationToken)
    {
        if (userId <= 0) return;
        await _userSettingRepository.SetAsync(userId, "ui.shell.shortcuts", configJson, cancellationToken);
    }
}
