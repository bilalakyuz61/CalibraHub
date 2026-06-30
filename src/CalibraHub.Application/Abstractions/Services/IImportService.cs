using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Şablon-tabanlı içe aktarım use-case'i (AI'sız). Şablon CRUD + Excel okuma +
/// önizleme/doğrulama + commit. Yazma, mevcut entity servislerine delege edilir
/// (Cari: <c>IFinanceService.UpsertContactAsync</c>) — tüm domain validasyonu korunur.
/// </summary>
public interface IImportService
{
    /// <summary>İçe aktarım yapılabilen hedef entity'ler (Cari, Stok, Cari İletişim, ...).</summary>
    IReadOnlyList<ImportEntityDto> GetEntities();

    /// <summary>Hedef entity için eşlenebilir alan kataloğu (statik).</summary>
    IReadOnlyList<ImportTargetFieldDto> GetTargetFields(string targetEntity);

    /// <summary>Statik + dinamik (form özel-alan/widget) birleşik alan kataloğu.</summary>
    Task<IReadOnlyList<ImportTargetFieldDto>> GetTargetFieldsAsync(string targetEntity, CancellationToken ct);

    Task<IReadOnlyList<ImportTemplateDto>> ListTemplatesAsync(bool includeInactive, CancellationToken ct);
    Task<ImportTemplateDto?> GetTemplateAsync(int id, CancellationToken ct);
    Task<(bool Ok, string? Error, int Id)> SaveTemplateAsync(SaveImportTemplateRequest req, int? userId, CancellationToken ct);
    Task DeleteTemplateAsync(int id, CancellationToken ct);
    Task<bool> ToggleTemplateAsync(int id, CancellationToken ct);

    /// <summary>Yüklenen dosyanın sayfa + başlık + ilk birkaç satırını döner (eşleme UI'si için).</summary>
    ImportHeaderReadDto ReadHeaders(byte[] data, string fileName, string? sheetName, int headerRowIndex);

    /// <summary>
    /// Kullanıcının doldurup geri yükleyeceği boş .xlsx şablonu üretir.
    /// templateId verilirse o şablonun kaynak kolonları, yoksa hedef alan etiketleri başlık olur.
    /// </summary>
    Task<(byte[] Bytes, string FileName)> BuildBlankTemplateAsync(string entity, int? templateId, CancellationToken ct);

    /// <summary>Şablonu dosyaya uygula, satır satır doğrula — kayıt YAZMAZ.
    /// <paramref name="overrides"/>: önizlemede elle düzeltilen hücreler (satırNo → alan → değer).</summary>
    Task<ImportPreviewResultDto> PreviewAsync(SaveImportTemplateRequest spec, byte[] data, string fileName,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string?>>? overrides, CancellationToken ct);

    /// <summary>Şablonu dosyaya uygula ve geçerli satırları kaydet (insert/update).
    /// <paramref name="overrides"/>: önizlemede elle düzeltilen hücreler (satırNo → alan → değer).
    /// <paramref name="excluded"/>: kullanıcının önizlemede iptal ettiği (hariç tuttuğu) satır no'ları.</summary>
    Task<ImportCommitResultDto> CommitAsync(SaveImportTemplateRequest spec, byte[] data, string fileName, int? userId,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, string?>>? overrides, IReadOnlyCollection<int>? excluded, CancellationToken ct);
}
