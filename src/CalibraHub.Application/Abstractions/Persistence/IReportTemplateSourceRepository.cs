using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Sablon data source'lari — cokki kaynakli sablon icin (Belge + Kombinasyon + Cari, vs.).
///
/// Geriye uyumluluk: Eski sablonlarda source kaydi YOKTUR; bu durumda caller,
/// template.SqlViewName + template.KeyColumn'dan virtual bir primary source insa edebilir.
/// </summary>
public interface IReportTemplateSourceRepository
{
    /// <summary>Belirli bir sablona bagli tum source'lari doner (DisplayOrder'a gore sirali).</summary>
    Task<IReadOnlyList<ReportTemplateSource>> GetByTemplateIdAsync(int templateId, CancellationToken cancellationToken);

    /// <summary>
    /// Mevcut source'lari KALDIRIP yeni listeyi topluca yazar. Atomic replace.
    /// UI'dan save'de kullanilir — kullanici ekle/sil isleminin sonucunu tek transaction'da yansitir.
    /// </summary>
    Task ReplaceAllAsync(int templateId, IReadOnlyList<ReportTemplateSource> sources, CancellationToken cancellationToken);

    /// <summary>Sablon silindiginde iliskili source'larin FK cascade ile silinmesi icin explicit trigger (dilege bagli).</summary>
    Task DeleteByTemplateIdAsync(int templateId, CancellationToken cancellationToken);
}
