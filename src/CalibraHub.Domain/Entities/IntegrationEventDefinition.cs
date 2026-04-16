using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class IntegrationEventDefinition : Entity
{
    public int CompanyId { get; init; }
    public required string Name { get; set; }
    public required string EventSource { get; set; }
    public required string EventType { get; set; }
    public string? EventDetail { get; set; }
    public string? SqlCommand { get; set; }
    public string ActionType { get; set; } = "SqlCommand";
    public string? ProcedureName { get; set; }
    public string? ParametersJson { get; set; }
    public string? ApiConfigJson { get; set; }
    public bool StopOnError { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int ExecutionOrder { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
