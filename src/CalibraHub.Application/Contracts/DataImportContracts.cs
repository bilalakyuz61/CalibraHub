using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

// ── Prosedür katmanı ─────────────────────────────────────────────────────

/// <summary>
/// Aktarım prosedürüne parametre olarak verilebilecek çalıştırma bağlamı.
/// Satır sayıları yalnız SON prosedürde doludur (ön prosedür henüz okumadan çalışır).
/// </summary>
public sealed record DataImportProcedureContext(
    long RunId,
    int JobId,
    string JobName,
    DateTime StartedAt,
    string? TriggeredBy,
    int RowsRead,
    int RowsInserted,
    int RowsUpdated,
    int RowsFailed);

/// <summary>Prosedür çalıştırma sonucu. Exception fırlatılmaz — hata bu kayda taşınır.</summary>
public sealed record DataImportProcedureResult(bool Ok, int AffectedRows, string? Error)
{
    public string Describe(string procedureName, DataImportProcedureTarget target)
    {
        var where = target == DataImportProcedureTarget.Source ? "kaynak DB" : "CalibraHub DB";
        return Ok
            ? $"{procedureName} ({where}): başarılı, {AffectedRows} satır."
            : $"{procedureName} ({where}): HATA — {Error}";
    }
}

// ── İş tanımı ────────────────────────────────────────────────────────────

public sealed record DataImportColumnDto(string SourceColumn, string TargetKey, int SortOrder);

public sealed record DataImportJobDto(
    int Id,
    string Name,
    int ConnectionId,
    string? ConnectionName,
    string TargetEntity,
    string? TargetEntityLabel,
    string SourceSchema,
    string SourceObject,
    string MatchKeyField,
    int MaxRows,
    string? SourceFilterJson,
    DataImportErrorBehavior ErrorBehavior,
    string? PreProcedureName,
    DataImportProcedureTarget PreProcedureTarget,
    string? PreProcedureParamsJson,
    string? PostProcedureName,
    DataImportProcedureTarget PostProcedureTarget,
    string? PostProcedureParamsJson,
    IReadOnlyList<DataImportColumnDto> Columns,
    bool IsActive,
    DateTime Created,
    DateTime? Updated);

public sealed record SaveDataImportJobRequest(
    int Id,
    string Name,
    int ConnectionId,
    string TargetEntity,
    string SourceSchema,
    string SourceObject,
    string MatchKeyField,
    int MaxRows,
    string? SourceFilterJson,
    DataImportErrorBehavior ErrorBehavior,
    string? PreProcedureName,
    DataImportProcedureTarget PreProcedureTarget,
    string? PreProcedureParamsJson,
    string? PostProcedureName,
    DataImportProcedureTarget PostProcedureTarget,
    string? PostProcedureParamsJson,
    IReadOnlyList<DataImportColumnDto> Columns,
    bool IsActive);

// ── Çalıştırma ───────────────────────────────────────────────────────────

public sealed record DataImportRunDto(
    long Id,
    int JobId,
    string? JobName,
    DataImportTriggerType TriggerType,
    DateTime StartedAt,
    DateTime? FinishedAt,
    int? DurationMs,
    DataImportRunStatus Status,
    int RowsRead,
    int RowsInserted,
    int RowsUpdated,
    int RowsFailed,
    string? ErrorMessage,
    string? PreProcedureResult,
    string? PostProcedureResult,
    string? TriggeredBy);

/// <summary>
/// Aktarım çalıştırma sonucu — çalıştırma kaydı + satır bazlı handler raporu.
/// </summary>
public sealed record DataImportRunResultDto(
    bool Ok,
    string? Error,
    DataImportRunDto? Run,
    ImportCommitResultDto? Commit);

/// <summary>
/// Önizleme sonucu: kaynaktan okunan örnek satırların handler doğrulamasından geçmiş hali.
/// Kayıt YAZILMAZ, prosedürler ÇALIŞTIRILMAZ.
/// </summary>
public sealed record DataImportPreviewResultDto(
    bool Ok,
    string? Error,
    int RowsRead,
    ImportPreviewResultDto? Preview);
