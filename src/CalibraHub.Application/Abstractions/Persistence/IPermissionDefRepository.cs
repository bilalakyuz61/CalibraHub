using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// 2026-06-06 — PermissionDef (izin katalog) CRUD. Impl: SqlPermissionDefRepository.
///
/// Form discovery sırasında PermissionDefDiscoveryService bu repository üzerinden upsert eder
/// (her FormCode için 6 standart action). Admin de manuel ekleyebilir (BUTTON:* özel action'lar).
/// </summary>
public interface IPermissionDefRepository
{
    Task<IReadOnlyList<PermissionDef>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<PermissionDef?> GetByIdAsync(int id, CancellationToken ct);
    Task<PermissionDef?> GetByFormAndActionAsync(string formCode, string actionCode, CancellationToken ct);

    /// <summary>FormCode'a göre filtreli liste — admin UI'da form ağacı + izin matrisi için.</summary>
    Task<IReadOnlyList<PermissionDef>> ListByFormAsync(string formCode, CancellationToken ct);

    /// <summary>Save (create veya update). FormCode + ActionCode unique — çakışırsa update path'i.</summary>
    Task<int> SaveAsync(PermissionDef entity, CancellationToken ct);

    /// <summary>
    /// Bulk upsert — discovery service her başlangıçta tüm form'lar için çağırır.
    /// Mevcut (FormCode, ActionCode) varsa Label/Category güncellenir, yoksa eklenir.
    /// </summary>
    Task BulkUpsertAsync(IReadOnlyList<PermissionDef> entities, CancellationToken ct);

    Task DeleteAsync(int id, CancellationToken ct);
}
