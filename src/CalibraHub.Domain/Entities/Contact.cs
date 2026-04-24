using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>Cari hesap — musteri, satici veya her ikisi.</summary>
[Description("Cari hesaplar (musteri + satici). Dokumanlarin ContactId FK'si bu tabloya baglanir; fiyat gruplari ve vergi bilgileri burada tutulur.")]
public sealed class Contact
{
    [Description("Birincil anahtar. IDENTITY.")]
    public int Id { get; init; }

    [Description("Cari hesap tipi (musteri/satici/her ikisi).")]
    public ContactType AccountType { get; init; } = ContactType.Customer;

    [Description("Kullanici tarafindan girilen benzersiz cari kod (rehberde/listede gosterilir).")]
    public required string AccountCode { get; init; }

    [Description("Cari unvani (kurum adi veya kisi adi).")]
    public required string AccountTitle { get; init; }

    [Description("Vergi numarasi (kurumsal cariler icin).")]
    public string? TaxNumber { get; init; }

    [Description("TC Kimlik numarasi (bireysel cariler icin).")]
    public string? IdentityNumber { get; init; }

    [Description("Kayitli vergi dairesi.")]
    public string? TaxOffice { get; init; }

    public string? Phone { get; init; }
    public string? Mobile { get; init; }
    public string? Email { get; init; }
    public string? Website { get; init; }
    public string? Address { get; init; }
    public string? PostalCode { get; init; }
    public string? City { get; init; }
    public string? District { get; init; }

    [Description("Mahalle/koy adi — PostalLocality cascade icin.")]
    public string? Neighborhood { get; init; }

    [Description("ISO 3166-1 alpha-2 ulke kodu (TR, US, DE, vs.). Opsiyonel.")]
    public string? CountryCode { get; init; }

    [Description("Cari tarafindan gorevlendirilmis primary contact person (ornegin: Ahmet Yilmaz).")]
    public string? ContactPerson { get; init; }

    [Description("Soft delete — kayit listede gosterilir mi?")]
    public bool IsActive { get; init; } = true;

    [Description("Bu cariye ait varsayilan fiyat grubu. FK -> PriceGroup.Id")]
    public int? PriceGroupId { get; init; }

    [Description("Bu cariyi takip eden satis temsilcisi. FK -> sales_representatives.id")]
    public int? SalesRepresentativeId { get; init; }

    public DateTime CreatedAt { get; init; }
}
