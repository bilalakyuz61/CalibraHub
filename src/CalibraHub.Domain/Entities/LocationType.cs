namespace CalibraHub.Domain.Entities;

/// <summary>
/// Lokasyon tipi tanimi (ornegin: Fabrika, Bolum, Raf, Hucre).
/// Lokasyonlar bu tiplerden birine baglanarak gruplanir.
/// </summary>
public sealed class LocationType
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;   // FACTORY, SECTION, ...
    public string Name { get; init; } = string.Empty;   // Fabrika, Bolum, ...
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}
