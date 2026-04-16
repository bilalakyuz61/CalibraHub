namespace CalibraHub.Application.Contracts;

public sealed record IntegrationEventDefinitionDto(
    Guid Id, int CompanyId, string Name, string EventSource, string EventType,
    string? EventDetail, string? SqlCommand, bool StopOnError, bool IsActive,
    int ExecutionOrder, DateTime CreatedAt, DateTime UpdatedAt,
    string ActionType, string? ProcedureName, string? ParametersJson, string? ApiConfigJson);

public sealed record SaveIntegrationEventRequest(
    Guid? Id, string Name, string EventSource, string EventType,
    string? EventDetail, string? SqlCommand, bool StopOnError, bool IsActive, int ExecutionOrder,
    string ActionType, string? ProcedureName, string? ParametersJson, string? ApiConfigJson);

public sealed record IntegrationEventLogDto(
    Guid Id, Guid DefinitionId, string EventSource, string EventType,
    string? ExecutedSql, string ActionType, string? ResponseBody,
    bool Success, string? ErrorMessage, DateTime ExecutedAt, long DurationMs);
