namespace CalibraHub.Application.Contracts;

/// <summary>Ülke satırı — şehir sayısıyla (silme guard'ı UI'da gösterilir).</summary>
public sealed record CountryDto(int Id, string? Code, string Name, bool IsActive, int CityCount);

/// <summary>Şehir satırı — ilçe sayısıyla.</summary>
public sealed record CityDto(int Id, int CountryId, string? Code, string Name, bool IsActive, int DistrictCount);

/// <summary>İlçe satırı.</summary>
public sealed record DistrictDto(int Id, int CityId, string? Code, string Name, bool IsActive);

/// <summary>Ülke/Şehir/İlçe ortak kaydetme isteği — Id null/0 ise yeni kayıt.
/// ParentId: City için CountryId, District için CityId; Country'de kullanılmaz.</summary>
public sealed record SaveAddressDefRequest(int? Id, int? ParentId, string Name);

/// <summary>Şehir board satırı — ülke adı + ilçe sayısıyla (tüm şehirler listesi).</summary>
public sealed record CityListDto(int Id, int CountryId, string CountryName, string Name, int DistrictCount);

/// <summary>İlçe board satırı — şehir + ülke adlarıyla (tüm ilçeler listesi).</summary>
public sealed record DistrictListDto(int Id, int CityId, string CityName, string CountryName, string Name);
