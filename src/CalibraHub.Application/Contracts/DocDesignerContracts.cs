namespace CalibraHub.Application.Contracts;

// ── Özet liste ────────────────────────────────────────────────────────────────
// DocType: legacy string code (backward-compat). NULL = "custom".
// DocumentTypeId: yeni FK (DocumentType.Id). NULL = "custom".
public sealed record DocLayoutSummaryDto(
    int Id,
    string Code,
    string Name,
    string? DocType,
    string? Description,
    bool IsDefault,
    int OwnerUserId,
    DateTime UpdatedAt,
    int? DocumentTypeId = null,
    string OutputFormat = "pdf",
    string? DefaultSubject = null,
    string? DefaultBody = null,
    // 2026-05-20: cikti turu (pdf/email) UI'dan kaldirildi. Mail sablonu artik
    // basit bir bayrak: acik dizaynlar mail compose ekraninda da listelenir.
    bool UseAsMailTemplate = false);

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
    string? DocType,
    string? Description,
    string LayoutJson,
    decimal PageW,
    decimal PageH,
    decimal MarginTop,
    decimal MarginBot,
    decimal MarginLeft,
    decimal MarginRight,
    bool IsDefault,
    int OwnerUserId,
    IReadOnlyCollection<DocLayoutDsDto> DataSources,
    int? DocumentTypeId = null,
    string OutputFormat = "pdf",
    string? DefaultSubject = null,
    string? DefaultBody = null,
    string? DefaultsViewName = null,
    string? DefaultsSubjectColumn = null,
    string? DefaultsBodyColumn = null,
    string? DefaultsWhere = null,
    // 2026-05-20: yeni bayrak — acik dizayn mail compose'da da seçilebilir.
    bool UseAsMailTemplate = false);

// ── Kaydetme isteği ───────────────────────────────────────────────────────────
// DocType ve DocumentTypeId birlikte gonderilir (frontend ikisini de set eder).
// Backend her ikisini de yazar; runtime DocType'i kullanmaya devam eder (PR scope).
public sealed record SaveDocLayoutRequest(
    int Id,
    string Code,
    string Name,
    string? DocType,
    string? Description,
    string LayoutJson,
    decimal PageW,
    decimal PageH,
    decimal MarginTop,
    decimal MarginBot,
    decimal MarginLeft,
    decimal MarginRight,
    bool IsDefault,
    IReadOnlyCollection<DocLayoutDsDto> DataSources,
    int? DocumentTypeId = null,
    string OutputFormat = "pdf",
    string? DefaultSubject = null,
    string? DefaultBody = null,
    string? DefaultsViewName = null,
    string? DefaultsSubjectColumn = null,
    string? DefaultsBodyColumn = null,
    string? DefaultsWhere = null,
    // 2026-05-20: yeni bayrak. Eski OutputFormat/Defaults* alanlar backward-compat
    // icin korundu — frontend artik bunlari göndermez; null/default gelir.
    bool UseAsMailTemplate = false);

// ── Render isteği ─────────────────────────────────────────────────────────────
public sealed record DocLayoutRunRequest(
    int LayoutId,
    int? DocumentId,
    IReadOnlyDictionary<string, string>? ParamOverrides);
