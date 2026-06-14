using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IRptRunLogRepository
{
    Task<long> LogStartAsync(
        int? defId,
        int viewId,
        int userId,
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
        int? userId,
        int top,
        CancellationToken ct);
}
