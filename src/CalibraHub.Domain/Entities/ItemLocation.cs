namespace CalibraHub.Domain.Entities;

/// <summary>
/// Malzeme karti ile lokasyon arasindaki cok-cogu iliski. Bir malzeme
/// birden fazla lokasyona atanabilir; bu lokasyonlardan biri varsayilan
/// olarak isaretlenir.
/// </summary>
public sealed class ItemLocation
{
    public int Id { get; init; }
    public int ItemId { get; init; }
    public int? LocationId { get; init; }
    public bool IsDefault { get; init; }
    public int SortOrder { get; init; }
}
