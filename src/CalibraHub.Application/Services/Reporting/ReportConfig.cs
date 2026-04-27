using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services.Reporting;

/// <summary>
/// ReportEngineService'in ConfigJson icin deserialize hedefi. Disariya kapali (internal).
/// Frontend bu sekli System.Text.Json ile uretir/tuketir; kolon isimleri camelCase.
/// </summary>
internal sealed record ReportConfig(
    ReportCategory Category,
    IReadOnlyCollection<ReportConfigColumn>? Columns,
    IReadOnlyCollection<ReportConfigGroup>? GroupBy,
    IReadOnlyCollection<ReportConfigAggregate>? Aggregates,
    IReadOnlyCollection<ReportConfigFilter>? Filters,
    IReadOnlyCollection<ReportConfigSort>? OrderBy,
    ReportChartOptions? Chart,
    ReportPivotOptions? Pivot,
    int? TopN,
    bool InjectContext);

internal sealed record ReportConfigColumn(string ColName, string? Alias);
internal sealed record ReportConfigGroup(string ColName);
internal sealed record ReportConfigAggregate(string ColName, ReportAggregate Fn, string? Alias);
internal sealed record ReportConfigFilter(string ColName, ReportFilterOperator Op, IReadOnlyCollection<object?> Values);
internal sealed record ReportConfigSort(string ColName, bool Descending);

internal sealed record ReportChartOptions(
    string Type,
    string XColumn,
    IReadOnlyCollection<string> YColumns,
    string? SeriesColumn);

internal sealed record ReportPivotOptions(
    IReadOnlyCollection<string> RowDimensions,
    IReadOnlyCollection<string> ColumnDimensions,
    string ValueColumn,
    ReportAggregate Aggregate);
