namespace CalibraHub.Application.Contracts;

public sealed record IntegratorImportLogWriteRequest(
    int IntegratorSettingsId,
    string IntegratorName,
    string Level,
    string Message,
    int ImportedCount,
    int SkippedCount,
    int? CompanyId = null,
    DateTime? OccurredAt = null);

public sealed record IntegratorImportLogEntryDto(
    DateTime OccurredAt,
    int IntegratorSettingsId,
    int? CompanyId,
    string CompanyName,
    string IntegratorName,
    string Level,
    string Message,
    int ImportedCount,
    int SkippedCount,
    string SourceFileName);
