using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Entegrasyon Wizard'in application servis katmanı. Wizard UI controller'ı
/// (`IntegrationsController`) bu servisi çağırır; repository'ye doğrudan inmez.
///
/// Sorumluluklar:
///   - List/Detail/Save/Delete/Toggle/Duplicate orchestration
///   - Validation (ad zorunlu, FormCode geçerli mi, mapping kuralları tutarlı mı)
///   - Aggregate kaydetme (Integration + Mappings + Triggers tek transaction'da)
///   - Audit log özetlerini (run count, last run) join'le sunma
/// </summary>
public interface IIntegrationService
{
    /// <summary>SmartBoard liste sayfası için. Mappings/Triggers yüklenmez (performance).</summary>
    Task<IReadOnlyList<IntegrationListItemDto>> ListAsync(bool includeInactive, CancellationToken ct);

    /// <summary>Wizard edit modu için tam aggregate.</summary>
    Task<IntegrationDetailDto?> GetDetailAsync(int id, CancellationToken ct);

    /// <summary>Yeni veya mevcut entegrasyonu kaydet. Mappings + Triggers replace edilir.</summary>
    Task<int> SaveAsync(SaveIntegrationRequest request, int? currentUserId, CancellationToken ct);

    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>IsActive toggle — soft enable/disable. Aggregate children korunur.</summary>
    Task<bool> ToggleActiveAsync(int id, CancellationToken ct);

    /// <summary>Mevcut entegrasyonun kopyasını oluştur (ad sonuna " (Kopya)" eklenir).</summary>
    Task<int> DuplicateAsync(int id, int? currentUserId, CancellationToken ct);

    /// <summary>Wizard Step 4 dry-run: mapping uygula + opsiyonel real send.</summary>
    Task<TestIntegrationResponse> TestAsync(TestIntegrationRequest request, string? currentUserName, CancellationToken ct);
}
