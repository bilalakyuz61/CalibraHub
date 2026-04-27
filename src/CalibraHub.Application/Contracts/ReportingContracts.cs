using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

// ── Caller context (built from claims in the controller) ───────────────────
public sealed record ReportCallerContext(
    Guid UserId,
    int CompanyId,
    IReadOnlyCollection<UserRole> Roles,
    IReadOnlyCollection<UserPermission> Permissions)
{
    public bool HasPermission(UserPermission permission) => Permissions.Contains(permission);
    public bool IsSystemAdmin => Roles.Contains(UserRole.SystemAdmin);
}

// ── View / column DTOs ─────────────────────────────────────────────────────
public sealed record ReportViewDto(
    int Id,
    string Code,
    string Name,
    string SqlObjectName,
    string? Description,
    bool IsActive);

public sealed record ReportViewColumnDto(
    int Id,
    string ColName,
    string DisplayName,
    ReportDataType DataType,
    bool IsFilterable,
    bool IsGroupable,
    bool IsAggregatable,
    ReportAggregate? DefaultAggregate,
    int Ordinal,
    ReportContextBinding ContextBinding);

public sealed record ReportViewRoleDto(UserRole Role, bool CanQuery, bool CanDesign);

public sealed record ReportViewDetailDto(
    ReportViewDto View,
    IReadOnlyCollection<ReportViewColumnDto> Columns,
    IReadOnlyCollection<ReportViewRoleDto> Roles);

public sealed record DiscoveredColumnDto(string ColName, string SqlType, bool IsNullable);

// ── Definition DTOs ────────────────────────────────────────────────────────
public sealed record ReportDefinitionSummaryDto(
    int Id,
    string Code,
    string Name,
    int ViewId,
    string ViewCode,
    ReportCategory Category,
    bool IsShared,
    Guid OwnerUserId,
    DateTime UpdatedAt);

public sealed record RptDefRoleDto(UserRole Role, bool CanView, bool CanEdit, bool CanDelete);

public sealed record ReportDefinitionDto(
    int Id,
    string Code,
    string Name,
    int ViewId,
    ReportCategory Category,
    string ConfigJson,
    Guid OwnerUserId,
    bool IsShared,
    bool IsActive,
    IReadOnlyCollection<RptDefRoleDto> Roles);

public sealed record SaveReportDefinitionRequest(
    int Id,
    string Code,
    string Name,
    int ViewId,
    ReportCategory Category,
    string ConfigJson,
    bool IsShared,
    IReadOnlyCollection<RptDefRoleDto> Roles);

// ── Admin upsert requests ──────────────────────────────────────────────────
public sealed record UpsertRptViewRequest(
    int Id,
    string Code,
    string Name,
    string SqlObjectName,
    string? Description,
    bool IsActive);

public sealed record UpsertRptViewColumnRequest(
    string ColName,
    string DisplayName,
    ReportDataType DataType,
    bool IsFilterable,
    bool IsGroupable,
    bool IsAggregatable,
    ReportAggregate? DefaultAggregate,
    int Ordinal,
    ReportContextBinding ContextBinding);

public sealed record UpsertRptViewRoleRequest(UserRole Role, bool CanQuery, bool CanDesign);

public sealed record UpsertRptDefinitionRequest(
    int Id,
    string Code,
    string Name,
    int ViewId,
    ReportCategory Category,
    string ConfigJson,
    Guid OwnerUserId,
    bool IsShared,
    bool IsActive);

// ── Execute pipeline ───────────────────────────────────────────────────────
public sealed record ReportFilterOverride(
    string ColName,
    ReportFilterOperator Operator,
    IReadOnlyCollection<object?> Values);

public sealed record ExecuteReportRequest(
    int? DefinitionId,
    int? ViewId,
    string? AdHocConfigJson,
    IReadOnlyCollection<ReportFilterOverride>? FilterOverrides,
    int? Page,
    int? PageSize);

public sealed record ReportResultColumn(
    string Name,
    string DisplayName,
    ReportDataType DataType,
    bool IsAggregate);

public sealed record ReportChartSeries(string Name, IReadOnlyList<object?> Values);

public sealed record ReportChartProjection(
    string ChartType,
    IReadOnlyList<string> Categories,
    IReadOnlyList<ReportChartSeries> Series);

public sealed record ReportExecutionResult(
    IReadOnlyCollection<ReportResultColumn> Columns,
    IReadOnlyCollection<IReadOnlyList<object?>> Rows,
    ReportChartProjection? Chart,
    int Page,
    int PageSize,
    int RowCount,
    string GeneratedSqlPreview,
    int DurationMs);

// ── Engine ↔ Executor contract (Application katmaninda kalir — SqlClient bagimliligini izole eder) ──
public sealed record ReportSqlParameter(string Name, ReportDataType? DataType, object? Value);
public sealed record ReportRawResult(
    IReadOnlyList<string> ColumnNames,
    IReadOnlyList<IReadOnlyList<object?>> Rows);
