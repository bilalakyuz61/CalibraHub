using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Form bazında ondalık ayarları. Şirket kimliği ICurrentCompanyProvider'dan alınır —
/// çağıranın CompanyId taşıması gerekmez, şirket yalnızca kendi ayarlarını görür.
///
/// Kullanım (hesaplama yapan her servis/ekran):
///   var dec = await _decimals.GetEffectiveAsync("SALES_QUOTE", ct);
///   line.Total = dec.RoundAmount(qty * price);
///
/// Yeni bir form/modül eklendiğinde ekstra iş gerekmez: form Forms tablosuna
/// kaydolduğu anda ayar ekranında listelenir; özel kaydı yoksa '*' varsayılanı,
/// o da yoksa sabit fallback (2,2,2,2,4) uygulanır.
/// </summary>
public interface IDecimalSettingService
{
    /// <summary>Etkili ayar: form kaydı → '*' varsayılan → fallback. Cache'li.</summary>
    Task<EffectiveDecimalsDto> GetEffectiveAsync(string? formCode, CancellationToken ct);

    /// <summary>Ayarlar ekranı: '*' satırı + Forms tablosundaki tüm formlar (LEFT JOIN ayar).</summary>
    Task<IReadOnlyList<DecimalSettingRowDto>> GetPageRowsAsync(CancellationToken ct);

    /// <summary>Upsert — 0-6 aralığına clamp edilir, cache düşürülür.</summary>
    Task SaveAsync(SaveDecimalSettingRequest request, int? userId, CancellationToken ct);

    /// <summary>Form kaydını sil → varsayılana dön ('*' silinemez, sadece güncellenir).</summary>
    Task ResetAsync(string formCode, CancellationToken ct);
}
