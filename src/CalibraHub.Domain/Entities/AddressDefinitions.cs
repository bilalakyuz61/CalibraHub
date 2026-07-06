using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Adres tanımlamaları hiyerarşisi: Country → City → District.
/// Genel Tanımlamalar sayfasındaki "Adres Tanımlama" grubundan yönetilir;
/// ileride ContactEdit İl/İlçe alanları buradan beslenecek.
/// Kod alanı kullanıcıdan alınmaz — Name'den otomatik türetilir (Türkiye seed'i 'TR').
/// Uniqueness: Country.Name global, City (CountryId+Name), District (CityId+Name).
/// </summary>
[Description("Ülke tanımı — adres hiyerarşisinin kökü. Türkiye kurulumda seed edilir.")]
public sealed class Country
{
    public int Id { get; init; }
    public string? Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}

[Description("Şehir tanımı — CountryId'ye bağlı; ad ülke içinde benzersiz.")]
public sealed class City
{
    public int Id { get; init; }
    public int CountryId { get; set; }
    public string? Code { get; set; }
    public required string Name { get; set; }
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
