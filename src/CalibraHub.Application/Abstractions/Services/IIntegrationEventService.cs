using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IIntegrationEventService
{
    /// <summary>
    /// Before eventleri: senkron calisir, StopOnError=true ise hata firlatir.
    /// </summary>
    Task ExecuteBeforeEventAsync(int companyId, string eventSource, string eventType,
        Dictionary<string, string>? placeholders, CancellationToken ct);

    /// <summary>
    /// After eventleri: fire-and-forget, hata yutulur ve loglanir.
    /// </summary>
    void FireAfterEvent(int companyId, string eventSource, string eventType,
        Dictionary<string, string>? placeholders);

    Task<IReadOnlyCollection<IntegrationEventDefinitionDto>> GetDefinitionsAsync(int companyId, CancellationToken ct);
    Task SaveDefinitionAsync(int companyId, SaveIntegrationEventRequest request, CancellationToken ct);
    Task DeleteDefinitionAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyCollection<IntegrationEventLogDto>> GetRecentLogsAsync(int companyId, int take, CancellationToken ct);

    // API Profile CRUD
    Task<IReadOnlyCollection<IntegrationApiProfileDto>> GetApiProfilesAsync(int companyId, CancellationToken ct);
    Task SaveApiProfileAsync(int companyId, SaveIntegrationApiProfileRequest request, CancellationToken ct);
    Task DeleteApiProfileAsync(Guid id, CancellationToken ct);
}
