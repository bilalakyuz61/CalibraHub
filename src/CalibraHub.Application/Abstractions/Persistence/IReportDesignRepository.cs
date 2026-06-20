using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IReportDesignRepository
{
    Task<int>                                   SaveAsync(SaveReportDesignRequest req, string? user, CancellationToken ct);
    Task                                        UpdateAsync(int id, SaveReportDesignRequest req, string? user, CancellationToken ct);
    Task                                        DeleteAsync(int id, CancellationToken ct);
    Task<(string Title, string PanelsJson, string? GroupName, string? Description)?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<ReportDesignSummaryDto>> GetAllAsync(CancellationToken ct);
}
