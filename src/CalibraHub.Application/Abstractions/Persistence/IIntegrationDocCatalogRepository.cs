using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Entegrasyon Doc Catalog veri erisim katmani — provider/enum/fieldDoc
/// CRUD ve okuma. Service tarafindan cache + seed ile sarilir.
/// </summary>
public interface IIntegrationDocCatalogRepository
{
    // ── Provider ──────────────────────────────────────────────────────────
    Task<IReadOnlyList<IntegrationProvider>> ListProvidersAsync(bool includeInactive, CancellationToken ct);
    Task<IntegrationProvider?> GetProviderByIdAsync(int id, CancellationToken ct);
    Task<IntegrationProvider?> GetProviderByCodeAsync(string code, CancellationToken ct);
    Task<int> UpsertProviderAsync(IntegrationProvider entity, int? actor, CancellationToken ct);
    Task DeleteProviderAsync(int id, int? actor, CancellationToken ct);

    // ── Enum Definition + Values ──────────────────────────────────────────
    Task<IReadOnlyList<IntegrationEnumDefinition>> ListEnumsAsync(int? providerId, bool includeInactive, CancellationToken ct);
    Task<IntegrationEnumDefinition?> GetEnumByIdAsync(int id, CancellationToken ct);
    Task<IntegrationEnumDefinition?> GetEnumByCodeAsync(int providerId, string code, CancellationToken ct);
    Task<int> UpsertEnumAsync(IntegrationEnumDefinition entity, int? actor, CancellationToken ct);
    Task DeleteEnumAsync(int id, int? actor, CancellationToken ct);

    // ── Field Doc ─────────────────────────────────────────────────────────
    Task<IReadOnlyList<IntegrationFieldDoc>> ListFieldDocsAsync(int? providerId, string? resource, bool includeInactive, CancellationToken ct);
    Task<IntegrationFieldDoc?> GetFieldDocByIdAsync(int id, CancellationToken ct);
    Task<IntegrationFieldDoc?> GetFieldDocByPathAsync(int providerId, string resource, string fieldPath, CancellationToken ct);
    Task<int> UpsertFieldDocAsync(IntegrationFieldDoc entity, int? actor, CancellationToken ct);
    Task DeleteFieldDocAsync(int id, int? actor, CancellationToken ct);

    /// <summary>
    /// 2026-05-21: Seed sonrası backfill — UsedInFieldPaths kolonu BOŞ olan enum'ları,
    /// kendisini referans veren FieldDoc'lardan otomatik doldurur. Yeni format JSON:
    /// <c>[{"endpointId": null, "path": "FatUst.Degissin"}, ...]</c>.
    /// Admin manuel düzenlediyse (UsedInFieldPaths zaten dolu) DOKUNULMAZ.
    /// Yeni enum + field-doc eklendikçe idempotent çalışır.
    /// </summary>
    /// <returns>Backfill yapılan enum sayısı.</returns>
    Task<int> BackfillEnumUsageFromFieldDocsAsync(int? actor, CancellationToken ct);
}
