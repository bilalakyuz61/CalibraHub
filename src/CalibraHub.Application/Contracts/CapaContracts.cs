namespace CalibraHub.Application.Contracts;

// ══════════════════════════════════════════════════════════════════
// DÖF (Düzeltici/Önleyici Faaliyet — CAPA)
// ══════════════════════════════════════════════════════════════════

public sealed record CapaListItemDto(
    int DocumentId, string DocumentNumber, string Title,
    byte CapaType, string CapaTypeLabel,
    byte Status, string StatusLabel,
    byte Severity, string SeverityLabel,
    int? ResponsiblePersonnelId, string? ResponsibleName,
    DateTime? DueDate, bool EffectivenessVerified, DateTime Created);

public sealed record CapaActionDto(
    int Id, byte ActionType, string ActionTypeLabel, string Description,
    int? ResponsiblePersonnelId, string? ResponsibleName, DateTime? DueDate,
    byte Status, string StatusLabel, DateTime? CompletedAt, int OrderNo);

public sealed record CapaDetailDto(
    int DocumentId, string DocumentNumber, byte CapaType,
    string? SourceKind, int? SourceId, string? SourceLabel, string? SourceUrl,
    string Title, string? ProblemDescription,
    int? DefectCodeId, string? DefectCodeName,
    byte Severity, byte? RootCauseMethod, string? RootCause,
    int? ResponsiblePersonnelId, string? ResponsibleName, DateTime? DueDate,
    byte Status,
    bool EffectivenessVerified, int? VerifiedByPersonnelId, string? VerifiedByName,
    DateTime? VerifiedAt, string? EffectivenessNote, DateTime? ClosedAt,
    IReadOnlyCollection<CapaActionDto> Actions);

/// <summary>DÖF kaydetme. Id=0 (DocumentId) yeni kayıt anlamına gelir (QualityInspection deseni).</summary>
public sealed record SaveCapaRequest(
    int Id, byte CapaType, string? SourceKind, int? SourceId,
    string Title, string? ProblemDescription, int? DefectCodeId, byte Severity,
    byte? RootCauseMethod, string? RootCause,
    int? ResponsiblePersonnelId, DateTime? DueDate,
    bool EffectivenessVerified, int? VerifiedByPersonnelId, DateTime? VerifiedAt, string? EffectivenessNote,
    IReadOnlyList<SaveCapaActionRequest> Actions);

public sealed record SaveCapaActionRequest(
    int Id, byte ActionType, string Description,
    int? ResponsiblePersonnelId, DateTime? DueDate,
    byte Status, DateTime? CompletedAt, int OrderNo);

/// <summary>Id = DocumentId (Capa'nın companion anahtarı QualityInspection deseniyle aynı).</summary>
public sealed record ChangeCapaStatusRequest(int Id, byte NewStatus);

public sealed record CapaPersonnelOption(int Id, string Name);

/// <summary>Kaynak kayıt arama sonucu (muayene vb.) — DÖF formu "Kaynak" seçici.</summary>
public sealed record CapaSourceLookupItem(string SourceKind, int SourceId, string Label, DateTime? Date);
