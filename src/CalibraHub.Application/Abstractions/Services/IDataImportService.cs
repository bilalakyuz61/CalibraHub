using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Veritabanı üzerinden içe aktarımın orkestrasyonu: kaynak bağlantı yönetimi, şema
/// keşfi, iş tanımı CRUD, önizleme ve çalıştırma.
///
/// Yazma işini KENDİSİ yapmaz — okunan satırları <c>ImportRowSet</c>'e çevirip ilgili
/// <see cref="IImportTargetHandler"/>'a devreder (Excel içe aktarımıyla ortak katman).
/// </summary>
public interface IDataImportService
{
    // ── Kaynak bağlantıları ──────────────────────────────────────────────
    Task<IReadOnlyList<ExternalDbConnectionDto>> ListConnectionsAsync(bool includeInactive, CancellationToken ct);
    Task<(bool Ok, string? Error, int Id)> SaveConnectionAsync(SaveExternalDbConnectionRequest req, int? userId, CancellationToken ct);
    Task DeleteConnectionAsync(int id, CancellationToken ct);
    Task<bool> ToggleConnectionActiveAsync(int id, CancellationToken ct);

    /// <summary>Kayıtlı bağlantıyı test eder.</summary>
    Task<ExternalDbTestResultDto> TestConnectionAsync(int connectionId, CancellationToken ct);

    /// <summary>Kaydetmeden önce, formdaki değerlerle test eder.</summary>
    Task<ExternalDbTestResultDto> TestConnectionDraftAsync(SaveExternalDbConnectionRequest req, CancellationToken ct);

    // ── Şema keşfi ───────────────────────────────────────────────────────
    Task<IReadOnlyList<ExternalDbObjectDto>> ListSourceObjectsAsync(int connectionId, CancellationToken ct);
    Task<IReadOnlyList<ExternalDbColumnDto>> ListSourceColumnsAsync(int connectionId, string schemaName, string objectName, CancellationToken ct);

    // ── Hedef entity kataloğu (handler'lardan) ───────────────────────────
    Task<IReadOnlyList<ImportEntityDto>> ListTargetEntitiesAsync(CancellationToken ct);
    Task<IReadOnlyList<ImportTargetFieldDto>> ListTargetFieldsAsync(string entity, CancellationToken ct);

    // ── İş tanımı ────────────────────────────────────────────────────────
    Task<IReadOnlyList<DataImportJobDto>> ListJobsAsync(bool includeInactive, CancellationToken ct);
    Task<DataImportJobDto?> GetJobAsync(int id, CancellationToken ct);
    Task<(bool Ok, string? Error, int Id)> SaveJobAsync(SaveDataImportJobRequest req, int? userId, CancellationToken ct);
    Task DeleteJobAsync(int id, CancellationToken ct);
    Task<bool> ToggleJobActiveAsync(int id, CancellationToken ct);

    // ── Önizleme / çalıştırma ────────────────────────────────────────────

    /// <summary>Kayıt YAZMAZ, prosedür ÇALIŞTIRMAZ — yalnız örnek satırları doğrular.</summary>
    Task<DataImportPreviewResultDto> PreviewAsync(int jobId, int sampleRows, CancellationToken ct);

    /// <summary>Ön prosedür → oku → yaz → son prosedür. Çalıştırma logu kaydedilir.</summary>
    Task<DataImportRunResultDto> RunAsync(int jobId, DataImportTriggerType trigger, string? triggeredBy, int? userId, CancellationToken ct);

    Task<IReadOnlyList<DataImportRunDto>> ListRunsAsync(int? jobId, int take, CancellationToken ct);
}
