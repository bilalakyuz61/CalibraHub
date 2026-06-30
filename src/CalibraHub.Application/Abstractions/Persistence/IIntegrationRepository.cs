using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>Form ekranı işlemler menüsü için entegrasyon buton metadata'sı.</summary>
public sealed record IntegrationManualButtonInfo(
    int Id,
    string Name,
    string? Description,
    string? ButtonLabel,
    string? ButtonColor,
    int? TargetEndpointId,
    string SourceFormCode);

/// <summary>
/// Integration aggregate ve ilişkili tablolar (Mapping, Trigger, Run, Endpoint) için
/// veri erişim arayüzü. Sprint 1 kapsamında temel CRUD; ileride filter/pagination eklenir.
///
/// Per-company DB üzerinde çalışır (SqlServerConnectionFactory ile HttpContext claim'inden
/// resolve). CompanyId parametresi yok — connection zaten doğru DB'ye bağlanır.
/// </summary>
public interface IIntegrationRepository
{
    // ── Integration (aggregate root) ──────────────────────────────────────
    /// <summary>Tüm entegrasyonları liste (aggregate children DOLDURULMAZ — performance).</summary>
    Task<IReadOnlyCollection<Integration>> ListAsync(bool includeInactive, CancellationToken ct);

    /// <summary>Aggregate (Mappings + Triggers + Endpoint) ile birlikte tek kayıt.</summary>
    Task<Integration?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>Form kodu için aktif entegrasyonları getirir. Manuel buton injection'da kullanılır.</summary>
    Task<IReadOnlyCollection<Integration>> ListByFormCodeAsync(string formCode, IntegrationTriggerType triggerType, CancellationToken ct);

    /// <summary>
    /// Form ekranı işlemler menüsüne enjekte edilecek manuel buton bilgilerini getirir.
    /// Trigger Config'inden buttonLabel / buttonColor ayrıştırılır.
    /// </summary>
    Task<IReadOnlyCollection<IntegrationManualButtonInfo>> ListManualButtonsAsync(string formCode, CancellationToken ct);

    /// <summary>Tüm formlardaki manuel buton bilgilerini döner — startup permission seeding için.</summary>
    Task<IReadOnlyCollection<IntegrationManualButtonInfo>> ListAllManualButtonsAsync(CancellationToken ct);

    /// <summary>
    /// Belirli bir trigger tipinde aktif olan tum entegrasyonlari getirir (form filtresi yok).
    /// Event/OnSave dispatcher'lari icin — caller config'lere bakip kendisi filtreler.
    /// Aggregate children DOLDURULMAZ (performance).
    /// </summary>
    Task<IReadOnlyCollection<Integration>> ListByTriggerTypeAsync(
        IntegrationTriggerType triggerType, CancellationToken ct);

    Task<int> AddAsync(Integration integration, CancellationToken ct);
    Task UpdateAsync(Integration integration, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>
    /// 2026-05-22 Cascade: Wizard Step 2 "Bağımlılık" dropdown'u için aktif + cascade'lenebilir
    /// integration'ları listeler. Aggregate children DOLDURULMAZ (sadece Id+Name+SourceFormCode
    /// yeterli). Filtre opsiyonel: formCode verilmişse sadece o forma ait olanlar.
    /// </summary>
    Task<IReadOnlyCollection<Integration>> ListCascadeTargetsAsync(
        string? sourceFormCode, CancellationToken ct);

    // ── Mapping ───────────────────────────────────────────────────────────
    Task<IReadOnlyCollection<IntegrationMapping>> GetMappingsAsync(int integrationId, CancellationToken ct);
    Task ReplaceMappingsAsync(int integrationId, IReadOnlyCollection<IntegrationMapping> mappings, CancellationToken ct);

    // ── Trigger ───────────────────────────────────────────────────────────
    Task<IReadOnlyCollection<IntegrationTrigger>> GetTriggersAsync(int integrationId, CancellationToken ct);
    Task ReplaceTriggersAsync(int integrationId, IReadOnlyCollection<IntegrationTrigger> triggers, CancellationToken ct);

    // ── Endpoint ──────────────────────────────────────────────────────────
    Task<IReadOnlyCollection<IntegrationEndpoint>> ListEndpointsAsync(bool includeInactive, CancellationToken ct);
    Task<IReadOnlyCollection<IntegrationEndpoint>> ListEndpointsByProfileAsync(Guid apiProfileId, CancellationToken ct);
    Task<IntegrationEndpoint?> GetEndpointByIdAsync(int id, CancellationToken ct);
    Task<int> AddEndpointAsync(IntegrationEndpoint endpoint, CancellationToken ct);
    Task UpdateEndpointAsync(IntegrationEndpoint endpoint, CancellationToken ct);
    Task DeleteEndpointAsync(int id, CancellationToken ct);

    // ── Run (audit log) ──────────────────────────────────────────────────
    Task<long> AddRunAsync(IntegrationRun run, CancellationToken ct);
    Task UpdateRunAsync(IntegrationRun run, CancellationToken ct);
    Task<IReadOnlyCollection<IntegrationRun>> GetRunsAsync(int integrationId, int limit, CancellationToken ct);
    Task<IntegrationRun?> GetLatestRunAsync(int integrationId, string sourceRecordId, CancellationToken ct);
    Task<IntegrationRun?> GetRunByIdAsync(long id, CancellationToken ct);

    /// <summary>
    /// Tum entegrasyonlardan run'lari listele (admin Run log sayfasi icin).
    /// Filtre: status (Success/Failed/Skipped/Retrying), integrationId, son N gun, limit.
    /// Sirali: StartedAt DESC.
    /// </summary>
    Task<IReadOnlyCollection<IntegrationRun>> ListAllRunsAsync(
        int? integrationId, string? status, int sinceDays, int limit, CancellationToken ct);

    /// <summary>
    /// Bir integration'a ait tüm IntegrationRun kayıtlarını siler. Hard delete akışında
    /// (IntegrationService.DeleteAsync) FK_IntegrationRun_Integration violation'ı önlemek için
    /// önce çağrılır. Soft delete tercih edenler Toggle ile pasif yapar; bu method çağrılmaz.
    /// </summary>
    Task<int> DeleteRunsForIntegrationAsync(int integrationId, CancellationToken ct);
}
