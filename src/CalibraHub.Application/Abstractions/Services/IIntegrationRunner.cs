using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Bir entegrasyonu calistirma orchestrator'i. Asagidaki adimlari yapar:
///   1. Integration aggregate'i yukle (mappings + triggers + endpoint)
///   2. Kaynak form kaydini cek (IFormMetadataService)
///   3. MappingEngine ile JSON body uret
///   4. HttpExecutor ile endpoint'e gonder
///   5. IntegrationRun audit log yaz (StartedAt, FinishedAt, Status, vb.)
///
/// Hata davranisi Integration.ErrorBehavior'e gore:
///   Skip   → loglar, return
///   Retry  → retry kuyruguna at (V1.5+ — V1 manuel retry)
///   Manual → "manuel inceleme" durumuna goz onunde
/// </summary>
public interface IIntegrationRunner
{
    Task<IntegrationRunnerResult> RunAsync(
        int integrationId,
        string? sourceRecordId,
        IntegrationTriggerType triggerType,
        string? triggeredBy,
        CancellationToken ct);
}

public sealed class IntegrationRunnerResult
{
    public bool Success { get; init; }
    public long RunId { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? RequestBody { get; init; }
    public string? ResponseBody { get; init; }
}
