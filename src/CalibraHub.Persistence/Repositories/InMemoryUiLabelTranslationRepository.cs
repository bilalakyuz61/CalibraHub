using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Persistence.Repositories;

public sealed class InMemoryUiLabelTranslationRepository : IUiLabelTranslationRepository
{
    private readonly InMemoryDataStore _dataStore;

    public InMemoryUiLabelTranslationRepository(InMemoryDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public Task<IReadOnlyCollection<UiLabelTranslation>> GetByLanguageAsync(
        string languageCode,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<UiLabelTranslation> result = _dataStore.UiLabelTranslations.Values
            .Where(x => string.Equals(x.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.FormKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LabelKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyCollection<UiLabelTranslation>> GetByFormAndLanguageAsync(
        string formKey,
        string languageCode,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<UiLabelTranslation> result = _dataStore.UiLabelTranslations.Values
            .Where(x =>
                string.Equals(x.FormKey, formKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.LabelKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(result);
    }

    public Task ReplaceFormLanguageAsync(
        string formKey,
        string languageCode,
        IReadOnlyCollection<UiLabelTranslation> translations,
        CancellationToken cancellationToken)
    {
        var existingIds = _dataStore.UiLabelTranslations
            .Where(x =>
                string.Equals(x.Value.FormKey, formKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Value.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Key)
            .ToArray();

        foreach (var existingId in existingIds)
        {
            _dataStore.UiLabelTranslations.TryRemove(existingId, out _);
        }

        foreach (var translation in translations)
        {
            _dataStore.UiLabelTranslations[translation.Id] = translation;
        }

        return Task.CompletedTask;
    }
}
