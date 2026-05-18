using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Entegrasyon Doc Catalog servisi — admin CRUD + wizard read.
/// Memory cache ile sarilmis (~5dk TTL), admin save sonrasi invalidate.
/// Seed: Resources/Integration/Seed/*.json dosyalarindan idempotent yukler.
/// </summary>
public interface IIntegrationDocCatalogService
{
    /// <summary>
    /// Wizard tarafi — bir provider+resource icin field path → doc (enum dahil)
    /// sozlugu doner. Cache hit'te 0 ms. Eslesme yoksa bos sozluk.
    /// </summary>
    Task<IReadOnlyDictionary<string, IntegrationFieldDocRuntimeDto>> GetFieldDocsAsync(
        string providerCode, string resource, CancellationToken ct);

    // ── Provider ──────────────────────────────────────────────────────────
    Task<IReadOnlyList<IntegrationProviderAdminDto>> ListProvidersAsync(bool includeInactive, CancellationToken ct);
    Task<int> SaveProviderAsync(SaveIntegrationProviderRequest req, string? actor, CancellationToken ct);
    Task DeleteProviderAsync(int id, string? actor, CancellationToken ct);

    // ── Enum Definition ───────────────────────────────────────────────────
    Task<IReadOnlyList<IntegrationEnumDefinitionAdminDto>> ListEnumsAsync(int? providerId, bool includeInactive, CancellationToken ct);
    Task<IntegrationEnumDefinitionAdminDto?> GetEnumAsync(int id, CancellationToken ct);
    Task<int> SaveEnumAsync(SaveIntegrationEnumDefinitionRequest req, string? actor, CancellationToken ct);
    Task DeleteEnumAsync(int id, string? actor, CancellationToken ct);

    // ── Field Doc ─────────────────────────────────────────────────────────
    Task<IReadOnlyList<IntegrationFieldDocAdminDto>> ListFieldDocsAsync(int? providerId, string? resource, bool includeInactive, CancellationToken ct);
    Task<IntegrationFieldDocAdminDto?> GetFieldDocAsync(int id, CancellationToken ct);
    Task<int> SaveFieldDocAsync(SaveIntegrationFieldDocRequest req, string? actor, CancellationToken ct);
    Task DeleteFieldDocAsync(int id, string? actor, CancellationToken ct);

    /// <summary>Disaridan cache invalidate — seed sonrasi vs.</summary>
    void InvalidateCache();
}
