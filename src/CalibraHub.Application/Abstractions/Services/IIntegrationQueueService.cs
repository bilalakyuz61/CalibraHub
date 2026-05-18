using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// "Aktarim Kuyrugu" sayfasinin veri kaynagi.
///
/// Bir entegrasyonun kaynak formuna gore base table kayitlarini IntegrationRecordStatus
/// tablosu ile LEFT JOIN ederek listeler. Boylece henuz hic gonderilmemis kayitlar
/// (status NULL = Pending) + son denemesi basarisiz olanlar (Failed) + kullanicinin
/// "haric tuttugu" kayitlar (Skipped) tek bir filtre boyutunda gosterilir.
/// </summary>
public interface IIntegrationQueueService
{
    /// <summary>
    /// Sol menude listelenecek entegrasyonlar — Manual tetikleyici varsa
    /// (otomatik calisanlar zaten kuyrukta gormez).
    /// </summary>
    Task<IReadOnlyList<QueueIntegrationDto>> ListManualIntegrationsAsync(CancellationToken ct);

    /// <summary>
    /// Bir entegrasyonun kuyruk satirlari — filtre, arama, paginasyon.
    /// </summary>
    Task<QueueListResult> ListAsync(
        int integrationId,
        QueueFilter filter,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct);
}

/// <summary>
/// Sol menu icin minimum entegrasyon bilgisi.
/// </summary>
public sealed record QueueIntegrationDto(
    int    IntegrationId,
    string Name,
    string FormCode,
    string? FormName,
    string? Icon,
    string? IconColor,
    int    PendingCount,
    int    FailedCount,
    int    SkippedCount);

/// <summary>
/// Bir kuyruk satiri — base table'in display alanlari + status meta.
/// </summary>
public sealed record QueueRowDto(
    string  RecordId,
    string? Code,
    string? Name,
    string  Status,        // "Pending" | "Failed" | "Sent" | "Skipped"
    DateTime? LastSentAt,
    string? LastError,
    int     AttemptCount,
    long?   LastRunId,
    string? SkippedBy,
    string? SkipReason,
    DateTime? SkippedAt);

public sealed record QueueListResult(
    IReadOnlyList<QueueRowDto> Rows,
    int TotalCount,
    int Pending,
    int Failed,
    int Sent,
    int Skipped);

/// <summary>
/// UI'da gosterilecek filtre. "Active" varsayilan — bekleyenler (Pending + Failed).
/// </summary>
public enum QueueFilter
{
    /// <summary>Pending + Failed — kullaniciya "kuyrukta" gozuken</summary>
    Active   = 0,
    Pending  = 1,
    Failed   = 2,
    Sent     = 3,
    Skipped  = 4,
    All      = 5,
}
