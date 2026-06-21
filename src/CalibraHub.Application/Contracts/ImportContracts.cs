namespace CalibraHub.Application.Contracts;

// ════════════════════════════════════════════════════════════════════════
// Şablon-tabanlı içe aktarım (AI'sız) DTO'ları — Cari pilotu (2026-06-20)
//   Akış: Şablon tanımla → Excel/CSV yükle → önizle/doğrula → kayıt → rapor
// ════════════════════════════════════════════════════════════════════════

// ── Hedef alan kataloğu (Cari pilot: sabit liste; ileride form metadata) ──
public sealed record ImportTargetFieldDto(
    string Key,             // "AccountTitle"
    string Label,           // "Cari Unvanı"
    string DataType,        // "string" | "type" (AccountType) | "int" | "decimal"
    bool IsRequired,        // unvan zorunlu
    bool CanBeMatchKey,     // upsert anahtarı olabilir mi
    string? Hint = null);

// ── Bir şablondaki tek kolon eşlemesi ────────────────────────────────────
public sealed record ImportColumnMapDto(
    string TargetKey,        // hedef alan ("AccountTitle")
    string? SourceColumn,    // kaynak başlık adı (null = sadece DefaultValue kullan)
    string? Transform = null,// "trim" | "upper" | "lower" | "digits"
    string? DefaultValue = null);

// ── Şablon (oku) + kaydet isteği ─────────────────────────────────────────
public sealed record ImportTemplateDto(
    int Id,
    string Name,
    string TargetEntity,
    string? SheetName,
    int HeaderRowIndex,
    string? MatchKeyField,
    IReadOnlyList<ImportColumnMapDto> Columns,
    bool IsActive,
    DateTime Created,
    DateTime? Updated);

public sealed record SaveImportTemplateRequest(
    int Id,
    string Name,
    string TargetEntity,
    string? SheetName,
    int HeaderRowIndex,
    string? MatchKeyField,
    IReadOnlyList<ImportColumnMapDto> Columns,
    bool IsActive = true);

// ── Excel/CSV başlık okuma (eşleme UI'si için) ───────────────────────────
public sealed record ImportSheetDto(string Name, int RowCount);

public sealed record ImportHeaderReadDto(
    bool Success,
    string? Error,
    IReadOnlyList<ImportSheetDto> Sheets,
    string? ActiveSheet,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> SampleRows);

// ── Önizleme (kayıt yazmadan doğrulama) ──────────────────────────────────
public sealed record ImportPreviewCellDto(string Target, string? Value);

public sealed record ImportPreviewRowDto(
    int RowNumber,                              // dosyadaki satır no (başlık sonrası 1..N)
    string Action,                              // "insert" | "update" | "error"
    IReadOnlyList<ImportPreviewCellDto> Cells,
    IReadOnlyList<string> Errors);

public sealed record ImportPreviewResultDto(
    bool Success,
    string? Error,
    int TotalRows,
    int ValidRows,
    int ErrorRows,
    int InsertCount,
    int UpdateCount,
    IReadOnlyList<string> ColumnKeys,           // gösterilecek hedef alanlar (sıralı)
    IReadOnlyList<string> ColumnLabels,
    IReadOnlyList<ImportPreviewRowDto> Rows);   // detay (ilk N satır)

// ── Commit (gerçek kayıt) ────────────────────────────────────────────────
public sealed record ImportCommitRowDto(
    int RowNumber,
    bool Ok,
    string Action,          // "insert" | "update" | "error"
    string? Error,
    int? RecordId);

public sealed record ImportCommitResultDto(
    bool Success,
    string? Error,
    int Inserted,
    int Updated,
    int Failed,
    IReadOnlyList<ImportCommitRowDto> Rows);
