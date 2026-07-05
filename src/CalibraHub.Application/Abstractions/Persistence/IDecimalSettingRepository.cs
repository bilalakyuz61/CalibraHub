using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// DecimalSetting erişimi. Tüm sorgular CompanyId filtrelidir — şirket yalnızca
/// kendi ayarlarını okur/yazar (çağıran ICurrentCompanyProvider'dan alır).
/// </summary>
public interface IDecimalSettingRepository
{
    /// <summary>Şirketin tüm ayar satırları ('*' dahil).</summary>
    Task<IReadOnlyList<DecimalSetting>> GetAllAsync(int companyId, CancellationToken ct);

    /// <summary>Tek form kaydı (yoksa null).</summary>
    Task<DecimalSetting?> GetAsync(int companyId, string formCode, CancellationToken ct);

    /// <summary>(CompanyId, FormCode) upsert — varsa günceller, yoksa ekler.</summary>
    Task UpsertAsync(DecimalSetting setting, CancellationToken ct);

    /// <summary>Form kaydını siler → form şirket varsayılanına ('*') düşer.</summary>
    Task DeleteAsync(int companyId, string formCode, CancellationToken ct);
}
