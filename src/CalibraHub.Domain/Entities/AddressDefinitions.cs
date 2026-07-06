using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Adres tanımlamaları hiyerarşisi: Country → City → District → Neighborhood/Village.
/// Genel Tanımlamalar sayfasındaki "Adres Tanımlama" grubundan yönetilir;
/// ileride ContactEdit İl/İlçe alanları buradan beslenecek.
///
/// Ülke kodu KULLANICI girişlidir (açık talep — kod-girilmez kuralının bilinçli
/// istisnası); şehir/ilçe/mahalle/köy kodları Name'den otomatik türetilir.
/// Köy: DistrictId zorunlu, NeighborhoodId opsiyonel — köy hem ilçe altında
/// mahalle ile aynı hizada hem mahalle altında tanımlanabilir.
/// </summary>
[Description("Ülke tanımı — kod kullanıcı girişli, para birimi Currency FK, yabancı isim. Türkiye seed'li.")]
public sealed class Country
{
    public int Id { get; init; }
    /// <summary>Ülke kodu (TR, DE...) — kullanıcı girer (bilinçli istisna).</summary>
    public string? Code { get; set; }
    public required string Name { get; set; }
    /// <summary>Uluslararası ad (Turkey, Germany...).</summary>
    public string? ForeignName { get; set; }
    /// <summary>Para birimi — dbo.Currency(Id) FK.</summary>
    public int? CurrencyId { get; set; }
    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

[Description("Şehir tanımı — CountryId'ye bağlı; plaka kodu; ad ülke içinde benzersiz.")]
public sealed class City
{
    public int Id { get; init; }
    public int CountryId { get; set; }
    public string? Code { get; set; }
    public required string Name { get; set; }
    /// <summary>İl plaka kodu (61, 34...).</summary>
    public string? PlateCode { get; set; }
    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

[Description("İlçe tanımı — CityId'ye bağlı; ad şehir içinde benzersiz.")]
public sealed class District
{
    public int Id { get; init; }
    public int CityId { get; set; }
    public string? Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

[Description("Mahalle tanımı — DistrictId'ye bağlı; ad ilçe içinde benzersiz.")]
public sealed class Neighborhood
{
    public int Id { get; init; }
    public int DistrictId { get; set; }
    public string? Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

[Description("Köy tanımı — DistrictId zorunlu; NeighborhoodId doluysa mahalle altında, boşsa ilçe altında (mahalle ile aynı hizada).")]
public sealed class Village
{
    public int Id { get; init; }
    public int DistrictId { get; set; }
    /// <summary>Doluysa köy bu mahallenin altındadır; boşsa doğrudan ilçe altında.</summary>
    public int? NeighborhoodId { get; set; }
    public string? Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
