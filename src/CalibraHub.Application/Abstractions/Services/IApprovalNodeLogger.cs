using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public record ApprovalNodeLogDto(
    long Id,
    int? NodeId,
    string? NodeType,
    string? NodeName,
    string Event,
    string? Detail,
    int? DurationMs,
    DateTime Created);

public interface IApprovalNodeLogger
{
    Task LogAsync(
        int instanceId,
        int flowId,
        int? nodeId,
        string? nodeType,
        string? nodeName,
        string eventType,
        string? detail,
        int? durationMs,
        CancellationToken ct);

    Task<IReadOnlyList<ApprovalNodeLogDto>> GetLogsAsync(int instanceId, CancellationToken ct);
}
