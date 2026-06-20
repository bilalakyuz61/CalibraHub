namespace CalibraHub.Application.Contracts;

// ── Kayıtlı SQL kaynağı ──────────────────────────────────────────────────────

public sealed record ReportSourceDto(
    int       Id,
    string    Name,
    string?   Description,
    string    SqlQuery,
    int       CacheTtlMinutes,
    bool      IsActive,
    DateTime  Created,
    string?   CreatedBy,
    bool      Materialize,
    DateTime? LastMaterialized,
    int?      MaterializedRows,
    string?   RefreshScheduleJson
);

public sealed record SaveReportSourceRequest(
    int?    Id,
    string  Name,
    string? Description,
    string  SqlQuery,
    int     CacheTtlMinutes,
    bool    Materialize,
    string? RefreshScheduleJson
);

// ── Tasarım kaydı ────────────────────────────────────────────────────────────

public sealed record SaveReportDesignRequest(
    string  Title,
    string  PanelsJson,
    string? GroupName,
    string? Description
);

// ── Sorgu sonucu ─────────────────────────────────────────────────────────────

public sealed record ReportQueryResult(
    IReadOnlyList<string>    Columns,
    IReadOnlyList<object?[]> Rows,
    int                      RowCount,
    bool                     FromCache,
    long                     ElapsedMs
);

// ── Tasarım özeti ─────────────────────────────────────────────────────────────

public sealed record ReportDesignSummaryDto(
    int      Id,
    string   Title,
    DateTime Created,
    string?  CreatedBy,
    string?  GroupName
);
