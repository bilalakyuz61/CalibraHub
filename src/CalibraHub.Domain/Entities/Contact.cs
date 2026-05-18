using System.ComponentModel;
using System.Text.RegularExpressions;
using CalibraHub.Domain.Common;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>Cari hesap — musteri, satici veya her ikisi.</summary>
[Description("Cari hesaplar (musteri + satici). Dokumanlarin ContactId FK'si bu tabloya baglanir; fiyat gruplari ve vergi bilgileri burada tutulur.")]
public sealed class Contact
{
    [Description("Birincil anahtar. IDENTITY.")]
    public int Id { get; init; }

    [Description("Carinin ait oldugu sirket. FK -> Company.Id. Kayit anindaki oturum kullanicisinin sirketinden otomatik doldurulur.")]
    public int CompanyId { get; set; }

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

    [Description("WhatsApp icin ayri numara (Mobile'dan farkli olabilir). Gelen mesaj eslestirme ve giden gonderim icin kullanilir.")]
    public string? WaPhone { get; init; }

    [Description("Cari'nin WhatsApp'ta gorunen adi — sohbet listesinde bu yazi gosterilir, AccountTitle ile farkli olabilir.")]
    public string? WaName { get; init; }
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

    [Description("Cari grubu (Toptan, Perakende, VIP vb.). FK -> CariGroup.Id. Tasarim kurallari ve benzeri filtrelerde gruba gore eslestirme yapilir.")]
    public int? ContactGroupId { get; init; }

    public DateTime CreatedAt { get; init; }

    // ── Davranis: Validation & Normalization (rapor §2.4) ────────────────────

    private static readonly Regex DigitsOnly = new(@"[^\d]", RegexOptions.Compiled);

    /// <summary>
    /// Cari entity'sinin tutarlilik (invariant) kontrolleri. Kayit ONCESI cagrilir.
    /// Hata: DomainException (caller try-catch ile yakalar).
    /// </summary>
    public void EnsureValid()
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(AccountCode),
            "Cari kodu zorunludur.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(AccountTitle),
            "Cari unvani zorunludur.");

        // Vergi numarasi: 10 hane (kurumsal)
        var taxDigits = NormalizeDigits(TaxNumber);
        DomainException.ThrowIf(!string.IsNullOrEmpty(taxDigits) && taxDigits.Length != 10,
            $"Vergi numarasi 10 hane olmalidir (girilen: {taxDigits.Length} hane).");

        // TC kimlik: 11 hane (bireysel)
        var idDigits = NormalizeDigits(IdentityNumber);
        DomainException.ThrowIf(!string.IsNullOrEmpty(idDigits) && idDigits.Length != 11,
            $"TC kimlik numarasi 11 hane olmalidir (girilen: {idDigits.Length} hane).");

        // WhatsApp numarasi: en az 10 hane (ulke kodu dahil)
        var waDigits = NormalizeDigits(WaPhone);
        DomainException.ThrowIf(!string.IsNullOrEmpty(waDigits) && waDigits.Length < 10,
            "WhatsApp numarasi en az 10 hane olmalidir (ulke kodu dahil).");

        // Email format (opsiyonel ama dolduysa @ ve . icermeli — basit kontrol)
        if (!string.IsNullOrWhiteSpace(Email))
        {
            DomainException.ThrowIf(!Email.Contains('@') || !Email.Contains('.'),
                $"Gecerli bir e-posta adresi giriniz: {Email}");
        }
    }

    /// <summary>Telefonu sadece rakamlara cevirir — "+90 (533) 444-5566" → "905334445566"</summary>
    public static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = DigitsOnly.Replace(raw, "");
        return string.IsNullOrEmpty(digits) ? null : digits;
    }

    private static string NormalizeDigits(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : DigitsOnly.Replace(raw, "");

    /// <summary>Kurumsal mi? (VKN var ve 10 hane).</summary>
    public bool IsCorporate() => !string.IsNullOrEmpty(NormalizeDigits(TaxNumber));

    /// <summary>Bireysel mi? (TCKN var ve 11 hane).</summary>
    public bool IsIndividual() => !string.IsNullOrEmpty(NormalizeDigits(IdentityNumber));
}
