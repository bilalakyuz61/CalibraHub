using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IRptRunLogRepository
{
    Task<long> LogStartAsync(
        int? defId,
        int viewId,
        Guid userId,
        int? companyId,
        byte[] sqlHash,
        CancellationToken ct);

    Task LogEndAsync(
        long id,
        int durationMs,
        int rowCount,
        string? error,
        CancellationToken ct);

    Task<IReadOnlyCollection<RptRunLog>> GetRecentAsync(
        int? defId,
        Guid? userId,
        int top,
        CancellationToken ct);
}
