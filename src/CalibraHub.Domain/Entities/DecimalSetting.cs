using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Form bazında ondalık hane ayarı. Ayarlar → Ondalık Ayarları ekranından yönetilir.
///
/// Çözümleme sırası (etkili ayar):
///   1) (CompanyId, FormCode) satırı          — form-spesifik ayar
///   2) (CompanyId, '*') satırı               — şirket geneli varsayılan
///   3) Hardcoded fallback (2,2,2,2,4)        — hiç kayıt yoksa
///
/// FormCode = Forms tablosundaki FormCode; '*' şirket geneli varsayılanı temsil eder.
/// Forms discovery'ye yeni eklenen her form ayar ekranında otomatik listelenir —
/// satırı olmayan form varsayılana düşer, yani yeni modüller altyapıya otomatik dahildir.
///
/// Tüm hesaplama noktaları (satır tutarı, toplamlar) bu ayarı IDecimalSettingService
/// üzerinden okur; frontend /DecimalSettings/EffectiveJson ile aynı değerleri alır.
/// </summary>
[Description("Form bazında ondalık hane ayarı — miktar/fiyat/tutar/oran/kur hassasiyeti.")]
public sealed class DecimalSetting
{
    public int Id { get; init; }

    /// <summary>Ayarların ait olduğu şirket. Şirket yalnızca kendi kayıtlarını okur.</summary>
    public int CompanyId { get; set; }

    /// <summary>Forms.FormCode veya '*' (şirket geneli varsayılan).</summary>
    public required string FormCode { get; set; }

    /// <summary>Miktar alanları ondalık hanesi (0-6).</summary>
    public int QuantityDecimals { get; set; } = 2;

    /// <summary>Birim fiyat alanları ondalık hanesi (0-6).</summary>
    public int UnitPriceDecimals { get; set; } = 2;

    /// <summary>Tutar/toplam alanları ondalık hanesi (0-6).</summary>
    public int AmountDecimals { get; set; } = 2;

    /// <summary>Oran/yüzde alanları ondalık hanesi (0-6).</summary>
    public int RateDecimals { get; set; } = 2;

    /// <summary>Döviz kuru ondalık hanesi (0-6).</summary>
    public int ExchangeRateDecimals { get; set; } = 4;

    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; set; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
