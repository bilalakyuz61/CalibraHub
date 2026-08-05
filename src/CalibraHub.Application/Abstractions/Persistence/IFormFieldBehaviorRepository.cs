using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Standart alan davranış tanımları (FormFieldBehavior) erişimi — form kodu başına
/// full-replace saklama (varsayılandan farklı davranışı olan satırlar tutulur).
/// </summary>
public interface IFormFieldBehaviorRepository
{
    Task<IReadOnlyCollection<FormFieldBehavior>> GetByFormCodeAsync(string formCode, CancellationToken ct);

    /// <summary>
    /// Formun tüm davranış satırlarını verilen listeyle DEĞİŞTİRİR (transaction:
    /// DELETE + INSERT). Boş liste = tüm davranışlar varsayılana döner.
    /// </summary>
    Task ReplaceForFormAsync(string formCode, IReadOnlyCollection<FormFieldBehavior> rows, CancellationToken ct);
}
