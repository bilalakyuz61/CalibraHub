namespace CalibraHub.Application.Auditing;

/// <summary>
/// Merkezi log ekranı arama isteği. <paramref name="FromUtc"/> dahil,
/// <paramref name="ToUtc"/> HARİÇ (exclusive) UTC anlarıdır — yerel gün aralığı
/// controller'da UTC'ye çevrilir.
/// </summary>
public sealed record AuditSearchRequest(
    DateTime FromUtc,
    DateTime ToUtc,
    string? Action = null,
    string? Entity = null,
    string? User = null,
    string? Text = null,
    int Page = 1,
    int PageSize = 50);

/// <summary>Arama sonucu + filtre facet'leri (taranan aralıktaki değerler).</summary>
public sealed record AuditSearchResult(
    IReadOnlyList<AuditEntry> Items,
    int Total,
    IReadOnlyList<string> Entities,
    IReadOnlyList<string> Users);

public sealed record AuditDayCount(string Day, int Count);
public sealed record AuditKeyCount(string Key, string Label, int Count);

/// <summary>İzleme ekranı üst kartları + mini grafik verisi.</summary>
public sealed record AuditStats(
    int Total,
    int Inserts,
    int Updates,
    int Deletes,
    int SecurityEvents,
    int DistinctUsers,
    IReadOnlyList<AuditDayCount> ByDay,
    IReadOnlyList<AuditKeyCount> TopEntities,
    IReadOnlyList<AuditKeyCount> TopUsers);

/// <summary>
/// Günlük JSONL dosyalarından log okuma/sorgulama servisi (aktif şirket kapsamında).
/// Dosyalar yeniden-eskiye taranır; satırlar önce ucuz substring ön-filtresinden geçer,
/// yalnızca aday satırlar JSON parse edilir.
/// </summary>
public interface IAuditQueryService
{
    Task<AuditSearchResult> SearchAsync(AuditSearchRequest request, CancellationToken ct);

    /// <summary>
    /// Tek kaydın tüm değişiklik geçmişi (belge üzerinden erişim). Tüm günler
    /// yeniden-eskiye taranır; <paramref name="maxItems"/> dolunca durur.
    /// </summary>
    Task<IReadOnlyList<AuditEntry>> GetRecordTrailAsync(string entity, string entityId,
        int maxItems, CancellationToken ct);

    Task<AuditStats> GetStatsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}
