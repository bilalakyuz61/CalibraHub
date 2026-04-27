using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IReportEngineService
{
    Task<IReadOnlyCollection<ReportViewDto>> ListViewsAsync(ReportCallerContext caller, CancellationToken ct);
    Task<ReportViewDetailDto?> GetViewAsync(int viewId, ReportCallerContext caller, CancellationToken ct);
    Task<IReadOnlyCollection<DiscoveredColumnDto>> DiscoverColumnsAsync(int viewId, ReportCallerContext caller, CancellationToken ct);

    Task<int> UpsertViewAsync(UpsertRptViewRequest req, ReportCallerContext caller, CancellationToken ct);
    Task ReplaceColumnsAsync(int viewId, IReadOnlyCollection<UpsertRptViewColumnRequest> cols, ReportCallerContext caller, CancellationToken ct);
    Task ReplaceViewRolesAsync(int viewId, IReadOnlyCollection<UpsertRptViewRoleRequest> roles, ReportCallerContext caller, CancellationToken ct);

    Task<IReadOnlyCollection<ReportDefinitionSummaryDto>> ListDefinitionsAsync(ReportCallerContext caller, CancellationToken ct);
    Task<ReportDefinitionDto?> GetDefinitionAsync(int defId, ReportCallerContext caller, CancellationToken ct);
    Task<int> SaveDefinitionAsync(SaveReportDefinitionRequest req, ReportCallerContext caller, CancellationToken ct);
    Task DeleteDefinitionAsync(int defId, ReportCallerContext caller, CancellationToken ct);

    Task<ReportExecutionResult> ExecuteAsync(ExecuteReportRequest req, ReportCallerContext caller, CancellationToken ct);

    /// <summary>
    /// Same pipeline as ExecuteAsync but streams CSV (RFC 4180) to the given writer. Requires ExportReports permission.
    /// Returns total row count + generated SQL preview for audit purposes.
    /// </summary>
    Task<(int RowCount, string GeneratedSqlPreview)> ExecuteCsvAsync(
        ExecuteReportRequest req,
        ReportCallerContext caller,
        TextWriter writer,
        CancellationToken ct);
}
