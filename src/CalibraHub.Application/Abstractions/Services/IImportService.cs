using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Şablon-tabanlı içe aktarım use-case'i (AI'sız). Şablon CRUD + Excel okuma +
/// önizleme/doğrulama + commit. Yazma, mevcut entity servislerine delege edilir
/// (Cari: <c>IFinanceService.UpsertContactAsync</c>) — tüm domain validasyonu korunur.
/// </summary>
public interface IImportService
{
    /// <summary>Hedef entity için eşlenebilir alan kataloğu. Pilot: "CONTACT".</summary>
    IReadOnlyList<ImportTargetFieldDto> GetTargetFields(string targetEntity);

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

    /// <summary>Şablonu dosyaya uygula, satır satır doğrula — kayıt YAZMAZ.</summary>
    Task<ImportPreviewResultDto> PreviewAsync(SaveImportTemplateRequest spec, byte[] data, string fileName, CancellationToken ct);

    /// <summary>Şablonu dosyaya uygula ve geçerli satırları kaydet (insert/update).</summary>
    Task<ImportCommitResultDto> CommitAsync(SaveImportTemplateRequest spec, byte[] data, string fileName, int? userId, CancellationToken ct);
}
