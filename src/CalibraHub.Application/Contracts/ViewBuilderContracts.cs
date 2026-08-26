namespace CalibraHub.Application.Contracts;

// ════════════════════════════════════════════════════════════════════════
// SQL View Yönetimi — DefinitionJson yapısı (görsel kurucu <-> DB sözleşmesi)
//   { baseObject:{schema,name,alias},
//     joins:[{type:'inner'|'left'|'right', schema,name,alias, on:[{leftAlias,leftCol,op:'=',rightAlias,rightCol}]}],
//     columns:[{kind:'column',sourceAlias,sourceCol,alias}|{kind:'calc',expression,alias}] }
// NOT: "where" alanı Faz 1 kapsamı dışıdır (CLAUDE.md Faz 3) — gelirse yoksayılır.
// ════════════════════════════════════════════════════════════════════════

public sealed record ViewObjectRefDto(string Schema, string Name, string Alias);

public sealed record ViewJoinOnDto(string LeftAlias, string LeftCol, string Op, string RightAlias, string RightCol);

public sealed record ViewJoinDto(
    string Type,   // inner|left|right
    string Schema,
    string Name,
    string Alias,
    IReadOnlyList<ViewJoinOnDto> On);

public sealed record ViewColumnDto(
    string Kind,   // column|calc
    string? SourceAlias,
    string? SourceCol,
    string? Expression,
    string Alias);

public sealed record ViewDefinitionJson(
    ViewObjectRefDto BaseObject,
    IReadOnlyList<ViewJoinDto>? Joins,
    IReadOnlyList<ViewColumnDto> Columns);

// ── Request'ler ─────────────────────────────────────────────────────────

public sealed record PreviewViewRequest(
    bool IsRawMode,
    ViewDefinitionJson? Definition,
    string? RawSql,
    string? ViewName = null,   // henüz seçilmemişse null — geçici prob adıyla test edilir
    int Top = 20);

public sealed record ApplyViewRequest(
    string ViewName,
    string Kind,      // SystemExtend|Custom
    bool IsRawMode,
    ViewDefinitionJson? Definition,
    string? RawSql);

// ── Sonuç DTO'ları ──────────────────────────────────────────────────────

public sealed record ViewGenerateResult(bool Success, string? Sql, IReadOnlyList<string> Errors);

public sealed record ViewColumnMetaDto(string Name, string SqlType, bool IsNullable);

public sealed record ViewPreviewResult(
    bool Success,
    IReadOnlyList<string> Errors,
    string? Sql,
    IReadOnlyList<ViewColumnMetaDto> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> SampleRows,
    long? TotalRowCount,
    long? BaseRowCount,
    decimal? CartesianFactor,
    bool CartesianWarning);

public sealed record ViewApplyResult(
    bool Success,
    IReadOnlyList<string> Errors,
    int? ViewDefinitionId,
    string? Sql,
    bool CartesianWarning,
    decimal? CartesianFactor);

public sealed record ViewListItemDto(
    string Schema,
    string Name,
    bool HasOverride,
    string? OverrideKind,
    bool OverrideIsActive,
    int? ViewDefinitionId,
    DateTime? OverrideUpdated,
    int? ColumnCount,        // sys.columns'tan — DB'de fiziksel karşılığı yoksa null (yalnız override kaydı)
    int? RevisionCount,      // ViewDefinitionRevision sayısı — override yoksa null
    string? OverrideUpdatedBy);

public sealed record ViewOverrideDto(
    int Id,
    string Kind,
    string? DefinitionJson,
    string GeneratedSql,
    bool IsRawMode,
    bool IsActive,
    DateTime Created,
    string? CreatedBy,
    DateTime? Updated,
    string? UpdatedBy,
    int RevisionCount);

public sealed record ViewDetailDto(
    string ViewName,
    bool ExistsInDb,
    string? CurrentSql,       // OBJECT_DEFINITION — null ise fiziksel view yok (henüz apply edilmemiş Custom taslağı)
    bool HasOverride,
    ViewOverrideDto? Override);

// ── Şema yardımcıları (görsel kurucu dropdown'ları) ────────────────────

public sealed record ViewSchemaTableDto(string Schema, string Name, string Kind); // Kind: "table"|"view"

public sealed record ViewSchemaColumnDto(string Name, string SqlType, bool IsNullable);
