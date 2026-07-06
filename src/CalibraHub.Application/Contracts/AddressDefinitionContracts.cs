namespace CalibraHub.Application.Contracts;

/// <summary>Ülke satırı — para birimi display + şehir sayısıyla.</summary>
public sealed record CountryDto(
    int Id, string? Code, string Name, string? ForeignName,
    int? CurrencyId, string? CurrencyCode, string? CurrencyName,
    bool IsActive, int CityCount);

/// <summary>Şehir satırı — plaka kodu + ilçe sayısıyla.</summary>
public sealed record CityDto(int Id, int CountryId, string? Code, string Name, string? PlateCode, bool IsActive, int DistrictCount);

/// <summary>İlçe satırı.</summary>
public sealed record DistrictDto(int Id, int CityId, string? Code, string Name, bool IsActive);

/// <summary>Mahalle satırı.</summary>
public sealed record NeighborhoodDto(int Id, int DistrictId, string? Code, string Name, bool IsActive);

/// <summary>Köy satırı — NeighborhoodId doluysa mahalle altında, boşsa ilçe altında.</summary>
public sealed record VillageDto(int Id, int DistrictId, int? NeighborhoodId, string? Code, string Name, bool IsActive);

/// <summary>Ülke/Şehir/İlçe/Mahalle/Köy ortak kaydetme isteği — Id null/0 ise yeni.
/// ParentId: City→CountryId, District→CityId, Neighborhood→DistrictId,
/// Village→DistrictId (NeighborhoodId doluysa mahalle altına bağlanır).
/// Code/CurrencyId/ForeignName yalnız Country'de, PlateCode yalnız City'de kullanılır.</summary>
public sealed record SaveAddressDefRequest(
    int? Id, int? ParentId, string Name,
    string? Code = null, int? CurrencyId = null, string? ForeignName = null,
    string? PlateCode = null, int? NeighborhoodId = null);

/// <summary>Şehir board satırı — ülke adı + plaka + ilçe sayısıyla (tüm şehirler).</summary>
public sealed record CityListDto(int Id, int CountryId, string CountryName, string Name, string? PlateCode, int DistrictCount);

/// <summary>İlçe board satırı — şehir + ülke adlarıyla (tüm ilçeler).</summary>
public sealed record DistrictListDto(int Id, int CityId, string CityName, string CountryName, string Name);

/// <summary>Mahalle board satırı — ilçe/şehir adları + köy sayısıyla (tüm mahalleler).</summary>
public sealed record NeighborhoodListDto(int Id, int DistrictId, string DistrictName, string CityName, string Name, int VillageCount);

/// <summary>Köy board satırı — bağlı olduğu mahalle (varsa) + ilçe/şehir adlarıyla.</summary>
public sealed record VillageListDto(int Id, int DistrictId, int? NeighborhoodId, string? NeighborhoodName, string DistrictName, string CityName, string Name);

/// <summary>Para birimi lookup satırı (Country edit dropdown).</summary>
public sealed record CurrencyLookupDto(int Id, string Code, string Name);
