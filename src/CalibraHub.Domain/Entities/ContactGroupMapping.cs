namespace CalibraHub.Domain.Entities;

/// <summary>Contact -> CariGroup 5 slotlu eslestirme (MaterialGroupMappings deseninin ayni).</summary>
public sealed class ContactGroupMapping
{
    public int Id { get; init; }
    public int ContactId { get; init; }
    public int SlotOrder { get; init; }
    public required string GroupCode { get; init; }
    public DateTime Created { get; init; }
}
