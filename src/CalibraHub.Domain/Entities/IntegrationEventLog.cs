using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class IntegrationEventLog : Entity
{
    public Guid DefinitionId { get; init; }
    public int CompanyId { get; init; }
    public required string EventSource { get; init; }
    public required string EventType { get; init; }
    public string? ExecutedSql { get; init; }
    public string ActionType { get; init; } = "SqlCommand";
    public string? ResponseBody { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;
    public long DurationMs { get; init; }
}
