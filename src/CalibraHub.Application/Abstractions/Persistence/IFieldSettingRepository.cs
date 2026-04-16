using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// FldSet tablosu persistence arayuzu — form sabit alanlarinin
/// rehber eslestirmesi ve ayarlari.
/// </summary>
public interface IFieldSettingRepository
{
    /// <summary>Bir formun tum alan ayarlarini getirir (admin UI icin).</summary>
    Task<IReadOnlyCollection<FieldSetting>> GetByFormIdAsync(int formId, CancellationToken ct);

    /// <summary>Bir rehberle eslesmis tum alanlari getirir (eslestirme modali icin).</summary>
    Task<IReadOnlyCollection<FieldSetting>> GetByGuideCodeAsync(string guideCode, CancellationToken ct);

    /// <summary>Tekil alan ayari ekle (Id=0) veya guncelle (Id>0).</summary>
    Task<int> UpsertAsync(UpsertFieldSettingRequest request, CancellationToken ct);

    /// <summary>
    /// GuideLookupCell Ayarlar paneli — FormCode+FieldKey ile MERGE.
    /// formId bilinmeden kayit; Forms tablosundan FormCode ile cozumlenir.
    /// </summary>
    Task<int> UpsertByFormCodeAsync(UpsertFieldSettingByFormCodeRequest request, CancellationToken ct);

    /// <summary>Toplu rehber eslestirme — eslestirme modali kaydet.</summary>
    Task BulkMapGuideAsync(BulkMapGuideRequest request, CancellationToken ct);

    /// <summary>Alan ayarini sil.</summary>
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>Runtime: form icin rehber baglantilari (GuideCode IS NOT NULL AND IsActive=1).</summary>
    Task<IReadOnlyCollection<FieldGuideBindingDto>> GetGuideBindingsForFormAsync(string formCode, CancellationToken ct);

    /// <summary>
    /// Alan kesfi: BaseTable uzerinden INFORMATION_SCHEMA.COLUMNS sorgusu.
    /// Donen kolon adlari zaten FldSet'te tanimli olanlari haric tutar.
    /// </summary>
    Task<IReadOnlyCollection<string>> DiscoverFieldsAsync(int formId, CancellationToken ct);
}
