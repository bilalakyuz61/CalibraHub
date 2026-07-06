using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Adres tanımlamaları erişimi (per-company DB): Ülke → Şehir → İlçe → Mahalle/Köy.
/// Ad benzersizliği ve çocuk-kayıt silme guard'ları repository'de uygulanır;
/// ihlallerde Türkçe mesajlı InvalidOperationException fırlatılır.
/// Köy: districtId zorunlu; neighborhoodId doluysa mahalle altında (districtId
/// mahalleden türetilir), boşsa ilçe altında (mahalle ile aynı hizada).
/// </summary>
public interface IAddressDefinitionRepository
{
    // ── Ülke ──
    Task<IReadOnlyList<CountryDto>> ListCountriesAsync(CancellationToken ct);
    Task<CountryDto?> GetCountryAsync(int id, CancellationToken ct);
    Task<int> SaveCountryAsync(int? id, string name, string? code, int? currencyId, string? foreignName, int? userId, CancellationToken ct);
    Task DeleteCountryAsync(int id, CancellationToken ct);

    // ── Şehir ──
    Task<IReadOnlyList<CityDto>> ListCitiesAsync(int countryId, CancellationToken ct);
    Task<IReadOnlyList<CityListDto>> ListAllCitiesAsync(CancellationToken ct);
    Task<CityDto?> GetCityAsync(int id, CancellationToken ct);
    Task<int> SaveCityAsync(int? id, int countryId, string name, string? plateCode, int? userId, CancellationToken ct);
    Task DeleteCityAsync(int id, CancellationToken ct);

    // ── İlçe ──
    Task<IReadOnlyList<DistrictDto>> ListDistrictsAsync(int cityId, CancellationToken ct);
    Task<IReadOnlyList<DistrictListDto>> ListAllDistrictsAsync(CancellationToken ct);
    Task<DistrictDto?> GetDistrictAsync(int id, CancellationToken ct);
    Task<int> SaveDistrictAsync(int? id, int cityId, string name, int? userId, CancellationToken ct);
    Task DeleteDistrictAsync(int id, CancellationToken ct);

    // ── Mahalle ──
    Task<IReadOnlyList<NeighborhoodDto>> ListNeighborhoodsAsync(int districtId, CancellationToken ct);
    Task<IReadOnlyList<NeighborhoodListDto>> ListAllNeighborhoodsAsync(CancellationToken ct);
    Task<NeighborhoodDto?> GetNeighborhoodAsync(int id, CancellationToken ct);
    Task<int> SaveNeighborhoodAsync(int? id, int districtId, string name, int? userId, CancellationToken ct);
    Task DeleteNeighborhoodAsync(int id, CancellationToken ct);

    // ── Köy ──
    Task<IReadOnlyList<VillageListDto>> ListAllVillagesAsync(CancellationToken ct);
    Task<VillageDto?> GetVillageAsync(int id, CancellationToken ct);
    Task<int> SaveVillageAsync(int? id, int districtId, int? neighborhoodId, string name, int? userId, CancellationToken ct);
    Task DeleteVillageAsync(int id, CancellationToken ct);

    /// <summary>Para birimi lookup (Country edit dropdown) — dbo.Currency aktifleri.</summary>
    Task<IReadOnlyList<CurrencyLookupDto>> ListCurrenciesLookupAsync(CancellationToken ct);
}
