using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("PTT posta kodu bazli denormalize adres hiyerarsi satiri — ulke/il/ilce/mahalle + posta kodu. Performans icin tek tabloda flat tutulur (join yok).")]
public sealed class PostalLocality
{
    public int Id { get; init; }

    /// <summary>ISO 3166-1 alpha-2. Varsayilan TR.</summary>
    public string CountryCode { get; init; } = "TR";

    /// <summary>Il plaka kodu (01-81) veya uluslararasi kod.</summary>
    public string? CityCode { get; init; }

    public required string CityName { get; init; }
    public required string DistrictName { get; init; }
    public required string NeighborhoodName { get; init; }

    /// <summary>PTT posta kodu (5 haneli).</summary>
    public string? PostalCode { get; init; }
}
