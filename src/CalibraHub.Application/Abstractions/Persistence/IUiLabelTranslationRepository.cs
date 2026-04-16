using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IUiLabelTranslationRepository
{
    Task<IReadOnlyCollection<UiLabelTranslation>> GetByLanguageAsync(
        string languageCode,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<UiLabelTranslation>> GetByFormAndLanguageAsync(
        string formKey,
        string languageCode,
        CancellationToken cancellationToken);

    Task ReplaceFormLanguageAsync(
        string formKey,
        string languageCode,
        IReadOnlyCollection<UiLabelTranslation> translations,
        CancellationToken cancellationToken);
}
