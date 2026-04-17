using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Reporting;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services;

/// <summary>
/// Dinamik raporlama motoru. ConfigJson → guvenli T-SQL → jenerik sonuc.
///
/// Savunma katmanlari: VIEW whitelist (RptView FK) → VIEW adi regex →
/// kolon whitelist (RptViewCol) → kapasite bayraklari → enum-only operator/aggregate →
/// parametrize bind (hicbir deger stringe gomulmez) → rol/izin ACL → context binding.
/// </summary>
public sealed partial class ReportEngineService : IReportEngineService
{
    private const int MaxPageSize = 1000;
    private const int MaxTopN = 10_000;
    private const int PivotColumnCardinalityLimit = 64;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    [GeneratedRegex(@"^vw_[A-Za-z0-9_]{1,120}$")]
    private static partial Regex SafeViewNameRegex();

    private readonly IRptViewRepository _views;
    private readonly IRptDefinitionRepository _defs;
    private readonly IRptRunLogRepository _runLog;
    private readonly IReportQueryExecutor _executor;
    private readonly ILogger<ReportEngineService> _logger;

    public ReportEngineService(
        IRptViewRepository views,
        IRptDefinitionRepository defs,
        IRptRunLogRepository runLog,
        IReportQueryExecutor executor,
        ILogger<ReportEngineService> logger)
    {
        _views = views;
        _defs = defs;
        _runLog = runLog;
        _executor = executor;
        _logger = logger;
    }

    // ── Metadata listing ──────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<ReportViewDto>> ListViewsAsync(ReportCallerContext caller, CancellationToken ct)
    {
        RequirePermission(caller, UserPermission.ViewReports);
        var views = await _views.GetAllAsync(includeInactive: caller.IsSystemAdmin, ct);
        var result = new List<ReportViewDto>();
        foreach (var v in views)
        {
            if (caller.IsSystemAdmin || await CanAccessViewAsync(v.Id, caller, canDesignRequired: false, ct))
                result.Add(ToDto(v));
        }
        return result;
    }

    public async Task<ReportViewDetailDto?> GetViewAsync(int viewId, ReportCallerContext caller, CancellationToken ct)
    {
        RequirePermission(caller, UserPermission.ViewReports);
        var view = await _views.GetByIdAsync(viewId, ct);
        if (view == null) return null;
        if (!caller.IsSystemAdmin && !await CanAccessViewAsync(viewId, caller, canDesignRequired: false, ct))
            return null;

        var cols = await _views.GetColumnsAsync(viewId, ct);
        var roles = await _views.GetRolesAsync(viewId, ct);
        return new ReportViewDetailDto(
            View: ToDto(view),
            Columns: cols.Select(ToDto).ToArray(),
            Roles: roles.Select(r => new ReportViewRoleDto(r.Role, r.CanQuery, r.CanDesign)).ToArray());
    }

    public async Task<IReadOnlyCollection<DiscoveredColumnDto>> DiscoverColumnsAsync(
        int viewId, ReportCallerContext caller, CancellationToken ct)
    {
        RequirePermission(caller, UserPermission.ManageReports);
        return await _views.DiscoverColumnsAsync(viewId, ct);
    }

    // ── Admin upserts ─────────────────────────────────────────────────────

    public Task<int> UpsertViewAsync(UpsertRptViewRequest req, ReportCallerContext caller, CancellationToken ct)
    {
        RequirePermission(caller, UserPermission.ManageReports);
        if (!SafeViewNameRegex().IsMatch(req.SqlObjectName))
            throw new ReportValidationException($"Gecersiz SQL view adi: {req.SqlObjectName}. 'vw_' ile baslamali.");
        if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            throw new ReportValidationException("Kod ve isim zorunludur.");
        return _views.UpsertViewAsync(req, ct);
    }

    public Task ReplaceColumnsAsync(int viewId, IReadOnlyCollection<UpsertRptViewColumnRequest> cols,
        ReportCallerContext caller, CancellationToken ct)
    {
        RequirePermission(caller, UserPermission.ManageReports);
        foreach (var c in cols)
        {
            if (string.IsNullOrWhiteSpace(c.ColName))
                throw new ReportValidationException("Kolon adi bos olamaz.");
            if (!Enum.IsDefined(c.DataType))
                throw new ReportValidationException($"Gecersiz veri tipi: {c.DataType}");
            if (c.DefaultAggregate.HasValue && !Enum.IsDefined(c.DefaultAggregate.Value))
                throw new ReportValidationException($"Gecersiz aggregate: {c.DefaultAggregate}");
            if (!Enum.IsDefined(c.ContextBinding))
                throw new ReportValidationException($"Gecersiz context binding: {c.ContextBinding}");
        }
        return _views.ReplaceColumnsAsync(viewId, cols, ct);
    }

    public Task ReplaceViewRolesAsync(int viewId, IReadOnlyCollection<UpsertRptViewRoleRequest> roles,
        ReportCallerContext caller, CancellationToken ct)
    {
        RequirePermission(caller, UserPermission.ManageReports);
        return _views.ReplaceRolesAsync(viewId, roles, ct);
    }

    // ── Definition CRUD ───────────────────────────────────────────────────

    public Task<IReadOnlyCollection<ReportDefinitionSummaryDto>> ListDefinitionsAsync(
        ReportCallerContext caller, CancellationToken ct)
    {
        RequirePermission(caller, UserPermission.ViewReports);
        return _defs.GetAccessibleAsync(caller.UserId, caller.Roles, ct);
    }

    public async Task<ReportDefinitionDto?> GetDefinitionAsync(int defId, ReportCallerContext caller, CancellationToken ct)
    {
        RequirePermission(caller, UserPermission.ViewReports);
        var def = await _defs.GetByIdAsync(defId, ct);
        if (def == null || !def.IsActive) return null;
        var roles = await _defs.GetRolesAsync(defId, ct);
        if (!CanViewDefinition(def, roles, caller)) return null;
        return new ReportDefinitionDto(
            Id: def.Id,
            Code: def.Code,
            Name: def.Name,
            ViewId: def.ViewId,
            Category: def.Category,
            ConfigJson: def.ConfigJson,
            OwnerUserId: def.OwnerUserId,
            IsShared: def.IsShared,
            IsActive: def.IsActive,
            Roles: roles.Select(r => new RptDefRoleDto(r.Role, r.CanView, r.CanEdit, r.CanDelete)).ToArray());
    }

    public async Task<int> SaveDefinitionAsync(SaveReportDefinitionRequest req, ReportCallerContext caller, CancellationToken ct)
    {
        RequirePermission(caller, UserPermission.DesignReports);

        if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            throw new ReportValidationException("Kod ve isim zorunludur.");
        if (!Enum.IsDefined(req.Category))
            throw new ReportValidationException($"Gecersiz kategori: {req.Category}");
        if (string.IsNullOrWhiteSpace(req.ConfigJson))
            throw new ReportValidationException("ConfigJson zorunludur.");

        var cfg = ParseConfig(req.ConfigJson);
        var view = await _views.GetByIdAsync(req.ViewId, ct)
            ?? throw new ReportValidationException("Secilen VIEW bulunamadi.");
        var cols = await _views.GetColumnsAsync(req.ViewId, ct);
        ValidateConfig(cfg, cols);

        Guid ownerId = caller.UserId;
        if (req.Id > 0)
        {
            var existing = await _defs.GetByIdAsync(req.Id, ct)
                ?? throw new ReportNotFoundException($"Rapor tanimi bulunamadi: {req.Id}");
            var existingRoles = await _defs.GetRolesAsync(req.Id, ct);
            if (!CanEditDefinition(existing, existingRoles, caller))
                throw new ReportAuthorizationException("Bu raporu duzenleme yetkiniz yok.");
            ownerId = existing.OwnerUserId;
        }

        var upsertReq = new UpsertRptDefinitionRequest(
            Id: req.Id,
            Code: req.Code,
            Name: req.Name,
            ViewId: req.ViewId,
            Category: req.Category,
            ConfigJson: req.ConfigJson,
            OwnerUserId: ownerId,
            IsShared: req.IsShared,
            IsActive: true);
        var id = await _defs.UpsertAsync(upsertReq, ct);
        await _defs.ReplaceRolesAsync(id, req.Roles ?? Array.Empty<RptDefRoleDto>(), ct);
        return id;
    }

    public async Task DeleteDefinitionAsync(int defId, ReportCallerContext caller, CancellationToken ct)
    {
        RequirePermission(caller, UserPermission.DesignReports);
        var def = await _defs.GetByIdAsync(defId, ct)
            ?? throw new ReportNotFoundException($"Rapor tanimi bulunamadi: {defId}");
        var roles = await _defs.GetRolesAsync(defId, ct);
        if (!CanDeleteDefinition(def, roles, caller))
            throw new ReportAuthorizationException("Bu raporu silme yetkiniz yok.");
        await _defs.SoftDeleteAsync(defId, ct);
    }

    // ── Execute ───────────────────────────────────────────────────────────

    public async Task<ReportExecutionResult> ExecuteAsync(ExecuteReportRequest req, ReportCallerContext caller, CancellationToken ct)
    {
        var ctx = await ResolveExecutionContextAsync(req, caller, ct);
        var built = BuildSql(ctx, req, caller, forCsv: false);

        var runId = await _runLog.LogStartAsync(ctx.Def?.Id, ctx.View.Id, caller.UserId, caller.CompanyId, built.SqlHash, ct);
        var sw = Stopwatch.StartNew();
        try
        {
            var raw = await _executor.ExecuteAsync(built.Sql, built.Parameters, ct);
            sw.Stop();

            var resultCols = new List<ReportResultColumn>(raw.ColumnNames.Count);
            foreach (var name in raw.ColumnNames)
            {
                var (displayName, dt, isAgg) = ResolveOutputColumn(ctx, built, name);
                resultCols.Add(new ReportResultColumn(name, displayName, dt, isAgg));
            }

            ReportChartProjection? chart = null;
            IReadOnlyCollection<ReportResultColumn> finalCols = resultCols;
            IReadOnlyCollection<IReadOnlyList<object?>> finalRows = raw.Rows;

            if (ctx.Config.Category == ReportCategory.Chart && ctx.Config.Chart != null)
                chart = BuildChart(resultCols, raw.Rows, ctx.Config.Chart);
            else if (ctx.Config.Category == ReportCategory.Pivot && ctx.Config.Pivot != null)
                (finalCols, finalRows) = BuildPivot(resultCols, raw.Rows, ctx.Config.Pivot);

            await _runLog.LogEndAsync(runId, (int)sw.ElapsedMilliseconds, raw.Rows.Count, null, ct);

            var (page, pageSize) = NormalizePaging(req);
            return new ReportExecutionResult(
                Columns: finalCols,
                Rows: finalRows,
                Chart: chart,
                Page: page,
                PageSize: pageSize,
                RowCount: finalRows.Count,
                GeneratedSqlPreview: built.Sql,
                DurationMs: (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _runLog.LogEndAsync(runId, (int)sw.ElapsedMilliseconds, 0, Truncate(ex.Message, 2000), ct);
            _logger.LogError(ex, "Report execution failed for view {ViewId} (def {DefId})", ctx.View.Id, ctx.Def?.Id);
            throw;
        }
    }

    public async Task<(int RowCount, string GeneratedSqlPreview)> ExecuteCsvAsync(
        ExecuteReportRequest req,
        ReportCallerContext caller,
        TextWriter writer,
        CancellationToken ct)
    {
        RequirePermission(caller, UserPermission.ExportReports);
        var ctx = await ResolveExecutionContextAsync(req, caller, ct);
        var built = BuildSql(ctx, req, caller, forCsv: true);

        var runId = await _runLog.LogStartAsync(ctx.Def?.Id, ctx.View.Id, caller.UserId, caller.CompanyId, built.SqlHash, ct);
        var sw = Stopwatch.StartNew();
        int rowCount = 0;
        try
        {
            await _executor.ExecuteStreamingAsync(
                built.Sql,
                built.Parameters,
                onHeaders: async (headers, _) =>
                {
                    await writer.WriteLineAsync(string.Join(',', headers.Select(CsvEscape)));
                },
                onRow: async (row, _) =>
                {
                    if (rowCount >= MaxTopN) return false;
                    await writer.WriteLineAsync(string.Join(',', row.Select(FormatCsvCell)));
                    rowCount++;
                    return true;
                },
                ct);
            await writer.FlushAsync(ct);
            sw.Stop();
            await _runLog.LogEndAsync(runId, (int)sw.ElapsedMilliseconds, rowCount, null, ct);
            return (rowCount, built.Sql);
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _runLog.LogEndAsync(runId, (int)sw.ElapsedMilliseconds, rowCount, Truncate(ex.Message, 2000), ct);
            throw;
        }
    }

    // ── Internal pipeline ─────────────────────────────────────────────────

    private sealed record ExecutionContext(
        RptView View,
        IReadOnlyCollection<RptViewColumn> Columns,
        RptDefinition? Def,
        ReportConfig Config);

    private async Task<ExecutionContext> ResolveExecutionContextAsync(
        ExecuteReportRequest req, ReportCallerContext caller, CancellationToken ct)
    {
        RptDefinition? def = null;
        ReportConfig cfg;
        int viewId;

        if (req.DefinitionId.HasValue)
        {
            RequirePermission(caller, UserPermission.ViewReports);
            def = await _defs.GetByIdAsync(req.DefinitionId.Value, ct)
                ?? throw new ReportNotFoundException($"Rapor tanimi bulunamadi: {req.DefinitionId}");
            if (!def.IsActive)
                throw new ReportNotFoundException($"Rapor tanimi aktif degil: {req.DefinitionId}");
            var defRoles = await _defs.GetRolesAsync(def.Id, ct);
            if (!CanViewDefinition(def, defRoles, caller))
                throw new ReportNotFoundException($"Rapor tanimi bulunamadi: {req.DefinitionId}");
            viewId = def.ViewId;
            cfg = ParseConfig(def.ConfigJson);
        }
        else if (req.ViewId.HasValue && !string.IsNullOrWhiteSpace(req.AdHocConfigJson))
        {
            RequirePermission(caller, UserPermission.DesignReports);
            viewId = req.ViewId.Value;
            cfg = ParseConfig(req.AdHocConfigJson);
        }
        else
        {
            throw new ReportValidationException("DefinitionId veya (ViewId + AdHocConfigJson) saglanmalidir.");
        }

        var view = await _views.GetByIdAsync(viewId, ct)
            ?? throw new ReportNotFoundException($"VIEW bulunamadi: {viewId}");
        if (!view.IsActive && !caller.IsSystemAdmin)
            throw new ReportNotFoundException($"VIEW aktif degil: {viewId}");
        if (!SafeViewNameRegex().IsMatch(view.SqlObjectName))
            throw new ReportValidationException($"VIEW adi guvensiz: {view.SqlObjectName}");
        if (!caller.IsSystemAdmin && !await CanAccessViewAsync(viewId, caller, canDesignRequired: def == null, ct))
            throw new ReportAuthorizationException("Bu VIEW uzerinde yetkiniz yok.");

        var cols = await _views.GetColumnsAsync(viewId, ct);
        ValidateConfig(cfg, cols);
        return new ExecutionContext(view, cols, def, cfg);
    }

    // ── SQL building ──────────────────────────────────────────────────────

    private sealed class BuiltSql
    {
        public string Sql { get; set; } = string.Empty;
        public List<ReportSqlParameter> Parameters { get; } = new();
        public byte[] SqlHash { get; set; } = Array.Empty<byte>();
        public Dictionary<string, string> AliasToCol { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AggregateAliases { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private BuiltSql BuildSql(ExecutionContext ctx, ExecuteReportRequest req, ReportCallerContext caller, bool forCsv)
    {
        var built = new BuiltSql();
        var colIndex = ctx.Columns.ToDictionary(c => c.ColName, StringComparer.OrdinalIgnoreCase);
        var cfg = ctx.Config;
        var schemaEscaped = _executor.CurrentSchema.Replace("]", "]]");
        var viewName = ctx.View.SqlObjectName;

        var sb = new StringBuilder();
        var hasAggregates = cfg.Aggregates is { Count: > 0 };
        var groupCols = cfg.GroupBy?.Select(g => g.ColName).ToArray() ?? Array.Empty<string>();

        if (cfg.Category == ReportCategory.Pivot && cfg.Pivot != null)
        {
            groupCols = cfg.Pivot.RowDimensions.Concat(cfg.Pivot.ColumnDimensions).ToArray();
            hasAggregates = true;
        }

        sb.Append("SELECT ");
        var (page, pageSize) = NormalizePaging(req);
        var useTopN = cfg.TopN.HasValue && cfg.TopN.Value > 0 && !forCsv && !req.Page.HasValue;
        if (useTopN)
        {
            var topN = Math.Min(cfg.TopN!.Value, MaxTopN);
            sb.Append("TOP (@__TopN) ");
            built.Parameters.Add(new ReportSqlParameter("@__TopN", ReportDataType.Integer, topN));
        }

        var projections = new List<string>();

        if (cfg.Category == ReportCategory.Pivot && cfg.Pivot != null)
        {
            foreach (var col in groupCols)
                projections.Add($"[{col}]");
            var aggSql = BuildAggregateExpression(cfg.Pivot.ValueColumn, cfg.Pivot.Aggregate);
            const string alias = "__value";
            projections.Add($"{aggSql} AS [{alias}]");
            built.AggregateAliases.Add(alias);
        }
        else if (hasAggregates)
        {
            foreach (var col in groupCols)
                projections.Add($"[{col}]");
            foreach (var agg in cfg.Aggregates!)
            {
                var alias = string.IsNullOrWhiteSpace(agg.Alias) ? agg.ColName + "_" + agg.Fn : agg.Alias;
                projections.Add($"{BuildAggregateExpression(agg.ColName, agg.Fn)} AS [{alias}]");
                built.AliasToCol[alias] = agg.ColName;
                built.AggregateAliases.Add(alias);
            }
        }
        else
        {
            var cols = cfg.Columns ?? Array.Empty<ReportConfigColumn>();
            foreach (var c in cols)
            {
                var alias = string.IsNullOrWhiteSpace(c.Alias) ? c.ColName : c.Alias;
                if (string.Equals(alias, c.ColName, StringComparison.Ordinal))
                    projections.Add($"[{c.ColName}]");
                else
                    projections.Add($"[{c.ColName}] AS [{alias}]");
                built.AliasToCol[alias] = c.ColName;
            }
        }

        sb.Append(string.Join(", ", projections));
        sb.Append($" FROM [{schemaEscaped}].[{viewName}]");

        var where = new List<string>();
        int pIdx = 0;

        var filters = new List<ReportConfigFilter>(cfg.Filters ?? Array.Empty<ReportConfigFilter>());
        if (req.FilterOverrides != null)
        {
            foreach (var ov in req.FilterOverrides)
            {
                filters.RemoveAll(f => string.Equals(f.ColName, ov.ColName, StringComparison.OrdinalIgnoreCase));
                filters.Add(new ReportConfigFilter(ov.ColName, ov.Operator, ov.Values));
            }
        }

        foreach (var f in filters)
        {
            var col = colIndex[f.ColName];
            where.Add(BuildFilterClause(f, col, built, ref pIdx));
        }

        if (cfg.InjectContext)
        {
            foreach (var col in ctx.Columns)
            {
                switch (col.ContextBinding)
                {
                    case ReportContextBinding.CompanyId:
                        where.Add($"[{col.ColName}] = @__CompanyId");
                        if (built.Parameters.All(p => p.Name != "@__CompanyId"))
                            built.Parameters.Add(new ReportSqlParameter("@__CompanyId", ReportDataType.Integer, caller.CompanyId));
                        break;
                    case ReportContextBinding.UserId:
                    case ReportContextBinding.OwnerUserId:
                        var pname = col.ContextBinding == ReportContextBinding.UserId ? "@__UserId" : "@__OwnerUserId";
                        if (built.Parameters.All(p => p.Name != pname))
                            built.Parameters.Add(new ReportSqlParameter(pname, null, caller.UserId));
                        where.Add($"[{col.ColName}] = {pname}");
                        break;
                }
            }
        }

        if (where.Count > 0)
        {
            sb.Append(" WHERE ");
            sb.Append(string.Join(" AND ", where));
        }

        if (hasAggregates && groupCols.Length > 0)
        {
            sb.Append(" GROUP BY ");
            sb.Append(string.Join(", ", groupCols.Select(c => $"[{c}]")));
        }

        var sorts = (cfg.OrderBy ?? Array.Empty<ReportConfigSort>()).ToList();
        if (sorts.Count == 0)
        {
            if (groupCols.Length > 0)
                sorts.Add(new ReportConfigSort(groupCols[0], Descending: false));
            else if (cfg.Columns is { Count: > 0 })
                sorts.Add(new ReportConfigSort(cfg.Columns.First().ColName, Descending: false));
        }
        if (sorts.Count > 0)
        {
            sb.Append(" ORDER BY ");
            sb.Append(string.Join(", ", sorts.Select(s => $"[{s.ColName}] {(s.Descending ? "DESC" : "ASC")}")));
        }

        if (!useTopN && !forCsv)
        {
            if (sorts.Count == 0) sb.Append(" ORDER BY 1");
            var offset = (page - 1) * pageSize;
            sb.Append(" OFFSET @__Offset ROWS FETCH NEXT @__PageSize ROWS ONLY");
            built.Parameters.Add(new ReportSqlParameter("@__Offset", ReportDataType.Integer, offset));
            built.Parameters.Add(new ReportSqlParameter("@__PageSize", ReportDataType.Integer, pageSize));
        }

        sb.Append(';');
        built.Sql = sb.ToString();
        built.SqlHash = SHA256.HashData(Encoding.UTF8.GetBytes(built.Sql));
        return built;
    }

    private static string BuildAggregateExpression(string colName, ReportAggregate fn) => fn switch
    {
        ReportAggregate.Count => "COUNT(*)",
        ReportAggregate.CountDistinct => $"COUNT(DISTINCT [{colName}])",
        ReportAggregate.Sum => $"SUM([{colName}])",
        ReportAggregate.Avg => $"AVG([{colName}])",
        ReportAggregate.Min => $"MIN([{colName}])",
        ReportAggregate.Max => $"MAX([{colName}])",
        _ => throw new ReportValidationException($"Gecersiz aggregate fonksiyonu: {fn}")
    };

    private static string BuildFilterClause(
        ReportConfigFilter filter,
        RptViewColumn col,
        BuiltSql built,
        ref int pIdx)
    {
        var colSql = $"[{col.ColName}]";
        var values = filter.Values ?? Array.Empty<object?>();

        switch (filter.Op)
        {
            case ReportFilterOperator.Equals:
            case ReportFilterOperator.NotEquals:
            case ReportFilterOperator.GreaterThan:
            case ReportFilterOperator.GreaterOrEqual:
            case ReportFilterOperator.LessThan:
            case ReportFilterOperator.LessOrEqual:
            {
                var op = filter.Op switch
                {
                    ReportFilterOperator.Equals => "=",
                    ReportFilterOperator.NotEquals => "<>",
                    ReportFilterOperator.GreaterThan => ">",
                    ReportFilterOperator.GreaterOrEqual => ">=",
                    ReportFilterOperator.LessThan => "<",
                    ReportFilterOperator.LessOrEqual => "<=",
                    _ => "="
                };
                var pname = $"@p{pIdx++}";
                built.Parameters.Add(new ReportSqlParameter(pname, col.DataType, CoerceValue(values.FirstOrDefault(), col.DataType, col.ColName)));
                return $"{colSql} {op} {pname}";
            }
            case ReportFilterOperator.Between:
            {
                if (values.Count != 2)
                    throw new ReportValidationException($"'{col.ColName}' icin Between 2 deger gerektirir.");
                var p1 = $"@p{pIdx++}";
                var p2 = $"@p{pIdx++}";
                built.Parameters.Add(new ReportSqlParameter(p1, col.DataType, CoerceValue(values.ElementAt(0), col.DataType, col.ColName)));
                built.Parameters.Add(new ReportSqlParameter(p2, col.DataType, CoerceValue(values.ElementAt(1), col.DataType, col.ColName)));
                return $"{colSql} BETWEEN {p1} AND {p2}";
            }
            case ReportFilterOperator.In:
            case ReportFilterOperator.NotIn:
            {
                if (values.Count == 0)
                    throw new ReportValidationException($"'{col.ColName}' icin {filter.Op} en az 1 deger gerektirir.");
                var parts = new List<string>();
                foreach (var v in values)
                {
                    var pname = $"@p{pIdx++}";
                    built.Parameters.Add(new ReportSqlParameter(pname, col.DataType, CoerceValue(v, col.DataType, col.ColName)));
                    parts.Add(pname);
                }
                var op = filter.Op == ReportFilterOperator.In ? "IN" : "NOT IN";
                return $"{colSql} {op} ({string.Join(", ", parts)})";
            }
            case ReportFilterOperator.Contains:
            case ReportFilterOperator.StartsWith:
            case ReportFilterOperator.EndsWith:
            {
                var raw = values.FirstOrDefault()?.ToString() ?? string.Empty;
                var pattern = filter.Op switch
                {
                    ReportFilterOperator.Contains => $"%{EscapeLike(raw)}%",
                    ReportFilterOperator.StartsWith => $"{EscapeLike(raw)}%",
                    _ => $"%{EscapeLike(raw)}"
                };
                var pname = $"@p{pIdx++}";
                built.Parameters.Add(new ReportSqlParameter(pname, ReportDataType.String, pattern));
                return $"{colSql} LIKE {pname} ESCAPE '\\'";
            }
            case ReportFilterOperator.IsNull:
                return $"{colSql} IS NULL";
            case ReportFilterOperator.IsNotNull:
                return $"{colSql} IS NOT NULL";
            default:
                throw new ReportValidationException($"Desteklenmeyen operator: {filter.Op}");
        }
    }

    private static object? CoerceValue(object? raw, ReportDataType dt, string colName)
    {
        if (raw is null) return null;
        if (raw is JsonElement je)
        {
            raw = je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => je.ToString()
            };
        }
        if (raw is null) return null;

        try
        {
            return dt switch
            {
                ReportDataType.String => raw.ToString(),
                ReportDataType.Integer => Convert.ToInt64(raw, CultureInfo.InvariantCulture),
                ReportDataType.Decimal => Convert.ToDecimal(raw, CultureInfo.InvariantCulture),
                ReportDataType.Date => raw is DateTime dtv ? dtv.Date : DateTime.Parse(raw.ToString()!, CultureInfo.InvariantCulture).Date,
                ReportDataType.DateTime => raw is DateTime dtv2 ? dtv2 : DateTime.Parse(raw.ToString()!, CultureInfo.InvariantCulture),
                ReportDataType.Boolean => Convert.ToBoolean(raw, CultureInfo.InvariantCulture),
                _ => raw.ToString()
            };
        }
        catch (Exception ex)
        {
            throw new ReportValidationException($"'{colName}' kolonu icin deger tipi uyumsuz: {raw}", ex);
        }
    }

    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    // ── Config parsing & validation ───────────────────────────────────────

    private static ReportConfig ParseConfig(string json)
    {
        try
        {
            var cfg = JsonSerializer.Deserialize<ReportConfig>(json, JsonOptions);
            return cfg ?? throw new ReportValidationException("ConfigJson bos veya gecersiz.");
        }
        catch (JsonException ex)
        {
            throw new ReportValidationException("ConfigJson cozumlenemedi: " + ex.Message, ex);
        }
    }

    private static void ValidateConfig(ReportConfig cfg, IReadOnlyCollection<RptViewColumn> cols)
    {
        if (!Enum.IsDefined(cfg.Category))
            throw new ReportValidationException($"Gecersiz kategori: {cfg.Category}");

        var byName = cols.ToDictionary(c => c.ColName, StringComparer.OrdinalIgnoreCase);

        void MustExist(string colName, string purpose)
        {
            if (!byName.ContainsKey(colName))
                throw new ReportValidationException($"'{colName}' kolonu VIEW metadatasinda yok ({purpose}).");
        }

        void MustHaveFlag(string colName, Func<RptViewColumn, bool> flag, string flagName)
        {
            MustExist(colName, flagName);
            if (!flag(byName[colName]))
                throw new ReportValidationException($"'{colName}' kolonu {flagName} icin isaretli degil.");
        }

        foreach (var c in cfg.Columns ?? Array.Empty<ReportConfigColumn>())
            MustExist(c.ColName, "projection");
        foreach (var g in cfg.GroupBy ?? Array.Empty<ReportConfigGroup>())
            MustHaveFlag(g.ColName, x => x.IsGroupable, "IsGroupable");
        foreach (var a in cfg.Aggregates ?? Array.Empty<ReportConfigAggregate>())
        {
            if (a.Fn != ReportAggregate.Count)
                MustHaveFlag(a.ColName, x => x.IsAggregatable, "IsAggregatable");
            if (!Enum.IsDefined(a.Fn) || a.Fn == ReportAggregate.None)
                throw new ReportValidationException($"'{a.ColName}' icin gecersiz aggregate: {a.Fn}");
        }
        foreach (var f in cfg.Filters ?? Array.Empty<ReportConfigFilter>())
        {
            MustHaveFlag(f.ColName, x => x.IsFilterable, "IsFilterable");
            if (!Enum.IsDefined(f.Op))
                throw new ReportValidationException($"'{f.ColName}' icin gecersiz operator: {f.Op}");
        }
        // Output isim seti: VIEW kolonlari + projection alias'lari + aggregate alias'lari.
        // ORDER BY ve Chart kolonlari bu setten herhangi birine referans verebilir.
        var outputNames = new HashSet<string>(byName.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var c in cfg.Columns ?? Array.Empty<ReportConfigColumn>())
        {
            var alias = string.IsNullOrWhiteSpace(c.Alias) ? c.ColName : c.Alias;
            outputNames.Add(alias);
        }
        foreach (var a in cfg.Aggregates ?? Array.Empty<ReportConfigAggregate>())
        {
            var alias = string.IsNullOrWhiteSpace(a.Alias) ? a.ColName + "_" + a.Fn : a.Alias;
            outputNames.Add(alias);
        }

        void MustExistOutput(string name, string purpose)
        {
            if (!outputNames.Contains(name))
                throw new ReportValidationException($"'{name}' kolonu cikti setinde yok ({purpose}).");
        }

        foreach (var s in cfg.OrderBy ?? Array.Empty<ReportConfigSort>())
            MustExistOutput(s.ColName, "OrderBy");
        if (cfg.Chart != null)
        {
            MustExistOutput(cfg.Chart.XColumn, "Chart.X");
            foreach (var y in cfg.Chart.YColumns) MustExistOutput(y, "Chart.Y");
            if (!string.IsNullOrWhiteSpace(cfg.Chart.SeriesColumn))
                MustExistOutput(cfg.Chart.SeriesColumn!, "Chart.Series");
        }
        if (cfg.Pivot != null)
        {
            foreach (var r in cfg.Pivot.RowDimensions) MustHaveFlag(r, x => x.IsGroupable, "Pivot.Row");
            foreach (var c in cfg.Pivot.ColumnDimensions) MustHaveFlag(c, x => x.IsGroupable, "Pivot.Col");
            MustHaveFlag(cfg.Pivot.ValueColumn, x => x.IsAggregatable, "Pivot.Value");
            if (!Enum.IsDefined(cfg.Pivot.Aggregate) || cfg.Pivot.Aggregate == ReportAggregate.None)
                throw new ReportValidationException($"Pivot aggregate gecersiz: {cfg.Pivot.Aggregate}");
        }
    }

    // ── Authorization helpers ─────────────────────────────────────────────

    private static void RequirePermission(ReportCallerContext caller, UserPermission required)
    {
        if (caller.IsSystemAdmin) return;
        if (!caller.HasPermission(required))
            throw new ReportAuthorizationException($"Izin yok: {required}");
    }

    private async Task<bool> CanAccessViewAsync(int viewId, ReportCallerContext caller, bool canDesignRequired, CancellationToken ct)
    {
        var roles = await _views.GetRolesAsync(viewId, ct);
        foreach (var r in roles)
        {
            if (!caller.Roles.Contains(r.Role)) continue;
            if (canDesignRequired ? r.CanDesign : r.CanQuery) return true;
        }
        return false;
    }

    private static bool CanViewDefinition(RptDefinition def, IReadOnlyCollection<RptDefinitionRole> roles, ReportCallerContext caller)
    {
        if (caller.IsSystemAdmin) return true;
        if (def.OwnerUserId == caller.UserId) return true;
        if (!def.IsShared) return false;
        return roles.Any(r => caller.Roles.Contains(r.Role) && r.CanView);
    }

    private static bool CanEditDefinition(RptDefinition def, IReadOnlyCollection<RptDefinitionRole> roles, ReportCallerContext caller)
    {
        if (caller.IsSystemAdmin) return true;
        if (def.OwnerUserId == caller.UserId) return true;
        return def.IsShared && roles.Any(r => caller.Roles.Contains(r.Role) && r.CanEdit);
    }

    private static bool CanDeleteDefinition(RptDefinition def, IReadOnlyCollection<RptDefinitionRole> roles, ReportCallerContext caller)
    {
        if (caller.IsSystemAdmin) return true;
        if (def.OwnerUserId == caller.UserId) return true;
        return def.IsShared && roles.Any(r => caller.Roles.Contains(r.Role) && r.CanDelete);
    }

    // ── Projection helpers ────────────────────────────────────────────────

    private static (string DisplayName, ReportDataType DataType, bool IsAggregate) ResolveOutputColumn(
        ExecutionContext ctx, BuiltSql built, string outputName)
    {
        var isAgg = built.AggregateAliases.Contains(outputName);
        if (built.AliasToCol.TryGetValue(outputName, out var baseCol))
        {
            var meta = ctx.Columns.FirstOrDefault(c => string.Equals(c.ColName, baseCol, StringComparison.OrdinalIgnoreCase));
            if (meta != null)
            {
                var dt = isAgg && meta.DataType == ReportDataType.Integer ? ReportDataType.Decimal : meta.DataType;
                return (meta.DisplayName, dt, isAgg);
            }
        }
        var direct = ctx.Columns.FirstOrDefault(c => string.Equals(c.ColName, outputName, StringComparison.OrdinalIgnoreCase));
        if (direct != null) return (direct.DisplayName, direct.DataType, false);
        return (outputName, ReportDataType.String, isAgg);
    }

    private static ReportChartProjection BuildChart(
        IReadOnlyList<ReportResultColumn> cols,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        ReportChartOptions options)
    {
        var xIdx = IndexOf(cols, options.XColumn);
        var yIndexes = options.YColumns.Select(y => IndexOf(cols, y)).ToArray();
        var seriesIdx = string.IsNullOrWhiteSpace(options.SeriesColumn) ? -1 : IndexOf(cols, options.SeriesColumn!);

        var categories = rows.Select(r => r[xIdx]?.ToString() ?? string.Empty).Distinct().ToList();

        var series = new List<ReportChartSeries>();
        if (seriesIdx >= 0)
        {
            var seriesKeys = rows.Select(r => r[seriesIdx]?.ToString() ?? string.Empty).Distinct().ToList();
            foreach (var key in seriesKeys)
            {
                foreach (var yi in yIndexes)
                {
                    var values = new List<object?>(categories.Count);
                    foreach (var cat in categories)
                    {
                        var match = rows.FirstOrDefault(r =>
                            (r[xIdx]?.ToString() ?? string.Empty) == cat &&
                            (r[seriesIdx]?.ToString() ?? string.Empty) == key);
                        values.Add(match != null ? match[yi] : null);
                    }
                    series.Add(new ReportChartSeries($"{key} — {cols[yi].DisplayName}", values));
                }
            }
        }
        else
        {
            foreach (var yi in yIndexes)
            {
                var values = new List<object?>(categories.Count);
                foreach (var cat in categories)
                {
                    var match = rows.FirstOrDefault(r => (r[xIdx]?.ToString() ?? string.Empty) == cat);
                    values.Add(match != null ? match[yi] : null);
                }
                series.Add(new ReportChartSeries(cols[yi].DisplayName, values));
            }
        }

        return new ReportChartProjection(options.Type, categories, series);
    }

    private static (IReadOnlyCollection<ReportResultColumn>, IReadOnlyCollection<IReadOnlyList<object?>>) BuildPivot(
        IReadOnlyList<ReportResultColumn> cols,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        ReportPivotOptions pivot)
    {
        var rowIndexes = pivot.RowDimensions.Select(r => IndexOf(cols, r)).ToArray();
        var colIndexes = pivot.ColumnDimensions.Select(c => IndexOf(cols, c)).ToArray();
        var valueIdx = -1;
        for (int i = 0; i < cols.Count; i++) if (cols[i].Name == "__value") { valueIdx = i; break; }
        if (valueIdx < 0)
            throw new ReportValidationException("Pivot sorgu sonucunda __value kolonu bulunamadi.");

        var colKeys = rows
            .Select(r => string.Join("|", colIndexes.Select(ci => r[ci]?.ToString() ?? string.Empty)))
            .Distinct()
            .ToList();
        if (colKeys.Count > PivotColumnCardinalityLimit)
            throw new ReportValidationException(
                $"Pivot kolon boyutu cok yuksek ({colKeys.Count} > {PivotColumnCardinalityLimit}). Filtre daraltin.");

        var resultCols = new List<ReportResultColumn>();
        foreach (var ri in rowIndexes) resultCols.Add(cols[ri]);
        foreach (var key in colKeys)
        {
            var colLabel = string.IsNullOrEmpty(key) ? pivot.ValueColumn : key;
            resultCols.Add(new ReportResultColumn(colLabel, colLabel, ReportDataType.Decimal, IsAggregate: true));
        }

        var groupedRows = rows
            .GroupBy(r => string.Join("|", rowIndexes.Select(ri => r[ri]?.ToString() ?? string.Empty)))
            .ToList();

        var outRows = new List<IReadOnlyList<object?>>();
        foreach (var g in groupedRows)
        {
            var row = new object?[resultCols.Count];
            var first = g.First();
            for (int i = 0; i < rowIndexes.Length; i++) row[i] = first[rowIndexes[i]];
            foreach (var r in g)
            {
                var key = string.Join("|", colIndexes.Select(ci => r[ci]?.ToString() ?? string.Empty));
                var colPos = rowIndexes.Length + colKeys.IndexOf(key);
                if (colPos >= rowIndexes.Length) row[colPos] = r[valueIdx];
            }
            outRows.Add(row);
        }
        return (resultCols, outRows);
    }

    private static int IndexOf(IReadOnlyList<ReportResultColumn> cols, string name)
    {
        for (int i = 0; i < cols.Count; i++)
            if (string.Equals(cols[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        throw new ReportValidationException($"Kolon sonuc setinde yok: {name}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static (int Page, int PageSize) NormalizePaging(ExecuteReportRequest req)
    {
        var page = req.Page.GetValueOrDefault(1);
        if (page < 1) page = 1;
        var pageSize = req.PageSize.GetValueOrDefault(50);
        if (pageSize < 1) pageSize = 50;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
    }

    private static string CsvEscape(string s)
    {
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private static string FormatCsvCell(object? v)
    {
        if (v is null) return string.Empty;
        var s = v switch
        {
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => v.ToString() ?? string.Empty
        };
        return CsvEscape(s);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);

    private static ReportViewDto ToDto(RptView v) =>
        new(v.Id, v.Code, v.Name, v.SqlObjectName, v.Description, v.IsActive);

    private static ReportViewColumnDto ToDto(RptViewColumn c) =>
        new(c.Id, c.ColName, c.DisplayName, c.DataType, c.IsFilterable, c.IsGroupable,
            c.IsAggregatable, c.DefaultAggregate, c.Ordinal, c.ContextBinding);
}
