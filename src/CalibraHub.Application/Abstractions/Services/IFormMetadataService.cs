using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Wizard Step 1 ve Step 4'un kaynak form ile ilgili meta + ornek veri ihtiyaci icin servis.
/// Mevcut CalibraHub altyapisini kullanir:
///   - dbo.Forms      → form listesi (Module/SubModule/BaseTable/BaseRecordKey)
///   - dbo.WidgetMas  → form alanlari (label, dataType, isRequired, isPlainField)
///   - v_Flat_{FormCode} → ornek kayit (base table + widget pivot, tek satirda)
///
/// Hicbir tablonun semasi bu servis tarafindan degistirilmez; sadece okur.
/// </summary>
public interface IFormMetadataService
{
    /// <summary>Wizard Step 1 dropdown'u — modul/submodul gruplama dahil tum aktif formlar.</summary>
    Task<IReadOnlyList<IntegrationFormDto>> ListFormsAsync(CancellationToken ct);

    /// <summary>FormCode ile bir formun temel bilgilerini cek.</summary>
    Task<IntegrationFormDto?> GetFormAsync(string formCode, CancellationToken ct);

    /// <summary>Bir formun tum alanlari (widget + sistem). Wizard Step 1'de tree view'da gosterilir.</summary>
    Task<IReadOnlyList<IntegrationFormFieldDto>> GetFieldsAsync(string formCode, CancellationToken ct);

    /// <summary>
    /// Bir kaydin tum alanlarinin degerlerini doner. v_Flat_{FormCode} view'indan okur.
    /// recordId null ise en son kayit (ORDER BY id DESC TOP 1) — sample preview icin.
    /// View yoksa veya kayit yoksa null doner.
    /// </summary>
    Task<IntegrationSampleRecordDto?> GetSampleRecordAsync(string formCode, string? recordId, CancellationToken ct);

    /// <summary>
    /// Kod bazlı cascade lookup: v_Flat_{formCode} view'inde fieldName = fieldValue olan
    /// kaydın Id'sini döner. CascadeByValue=true olan mapping'lerde kod → entity ID çevirimi.
    /// View veya kayıt bulunamazsa null döner.
    /// </summary>
    Task<string?> FindRecordIdByFieldValueAsync(string formCode, string fieldName, string fieldValue, CancellationToken ct);
}
