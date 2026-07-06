using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Ülke/Şehir/İlçe tanımlamaları erişimi (per-company DB).
/// Ad benzersizliği ve çocuk-kayıt silme guard'ları repository'de uygulanır;
/// ihlallerde Türkçe mesajlı InvalidOperationException fırlatılır.
/// </summary>
public interface IAddressDefinitionRepository
{
    Task<IReadOnlyList<CountryDto>> ListCountriesAsync(CancellationToken ct);
    Task<int> SaveCountryAsync(int? id, string name, int? userId, CancellationToken ct);
    Task DeleteCountryAsync(int id, CancellationToken ct);

    Task<IReadOnlyList<CityDto>> ListCitiesAsync(int countryId, CancellationToken ct);
    Task<int> SaveCityAsync(int? id, int countryId, string name, int? userId, CancellationToken ct);
    Task DeleteCityAsync(int id, CancellationToken ct);

    Task<IReadOnlyList<DistrictDto>> ListDistrictsAsync(int cityId, CancellationToken ct);
    Task<int> SaveDistrictAsync(int? id, int cityId, string name, int? userId, CancellationToken ct);
    Task DeleteDistrictAsync(int id, CancellationToken ct);
}
