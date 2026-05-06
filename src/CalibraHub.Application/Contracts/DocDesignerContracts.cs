namespace CalibraHub.Application.Contracts;

// ── Özet liste ────────────────────────────────────────────────────────────────
public sealed record DocLayoutSummaryDto(
    int Id,
    string Code,
    string Name,
    string DocType,
    string? Description,
    bool IsDefault,
    Guid OwnerUserId,
    DateTime UpdatedAt);

// ── Veri kaynağı ──────────────────────────────────────────────────────────────
public sealed record DocLayoutDsDto(
    int Id,
    int LayoutId,
    string Alias,
    string Role,
    int? ViewId,
    string? AdHocSql,
    string? JoinOn,
    string? ParentAlias,
    int Ordinal);

// ── Tam detay (GET /layouts/{id}) ─────────────────────────────────────────────
public sealed record DocLayoutDetailDto(
    int Id,
    string Code,
    string Name,
    string DocType,
    string? Description,
    string LayoutJson,
    decimal PageW,
    decimal PageH,
    decimal MarginTop,
    decimal MarginBot,
    decimal MarginLeft,
    decimal MarginRight,
    bool IsDefault,
    Guid OwnerUserId,
    IReadOnlyCollection<DocLayoutDsDto> DataSources);

// ── Kaydetme isteği ───────────────────────────────────────────────────────────
public sealed record SaveDocLayoutRequest(
    int Id,
    string Code,
    string Name,
    string DocType,
    string? Description,
    string LayoutJson,
    decimal PageW,
    decimal PageH,
    decimal MarginTop,
    decimal MarginBot,
    decimal MarginLeft,
    decimal MarginRight,
    bool IsDefault,
    IReadOnlyCollection<DocLayoutDsDto> DataSources);

// ── Render isteği ─────────────────────────────────────────────────────────────
public sealed record DocLayoutRunRequest(
    int LayoutId,
    int? DocumentId,
    IReadOnlyDictionary<string, string>? ParamOverrides);
