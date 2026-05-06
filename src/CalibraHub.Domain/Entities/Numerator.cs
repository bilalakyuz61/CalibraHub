using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Sirket bazli numerator/sayac state'i. (CompanyId, EntityType) UNIQUE — her belge tipi icin tek satir. Format mask'i CompanyParameter'dan okunur (formCode = entityType, paramKey = 'NumeratorMask').")]
public sealed class Numerator
{
    public int Id { get; init; }
    public int CompanyId { get; init; }
    public required string EntityType { get; init; }
    public int CurrentValue { get; init; }
    public DateTime? LastResetAt { get; init; }
}
