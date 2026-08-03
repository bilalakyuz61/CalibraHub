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

/// <summary>DÖF yapısal kök neden analiz satırı (Faz 2). Method=5Neden'de Category NULL gelir;
/// Method=Ishikawa'da Category dolu (6M) ve CategoryLabel [Description] etiketiyle döner.</summary>
public sealed record CapaRootCauseItemDto(
    int Id, byte Method, byte? Category, string? CategoryLabel, int Sequence, string Text);

/// <summary>
/// DÖF DIŞ SÖZLEŞME — CapaDetailDto pozisyonel record'dur, repo'da named-arg ile construct
/// edilir. Yeni alan eklerken SONA ekle (Actions'tan sonra) — mevcut named-arg çağrıları bozulmasın.
/// </summary>
public sealed record CapaDetailDto(
    int DocumentId, string DocumentNumber, byte CapaType,
    string? SourceKind, int? SourceId,
    string Title, string? ProblemDescription,
    int? DefectCodeId, string? DefectCodeName,
    byte Severity, byte? RootCauseMethod, string? RootCause,
    int? ResponsiblePersonnelId, string? ResponsibleName, DateTime? DueDate,
    byte Status,
    bool EffectivenessVerified, int? VerifiedByPersonnelId, string? VerifiedByName,
    DateTime? VerifiedAt, string? EffectivenessNote, DateTime? ClosedAt,
    IReadOnlyCollection<CapaActionDto> Actions,
    IReadOnlyCollection<CapaRootCauseItemDto> RootCauseItems);

/// <summary>DÖF kaydetme. Id=0 (DocumentId) yeni kayıt anlamına gelir (QualityInspection deseni).</summary>
public sealed record SaveCapaRequest(
    int Id, byte CapaType, string? SourceKind, int? SourceId,
    string Title, string? ProblemDescription, int? DefectCodeId, byte Severity,
    byte? RootCauseMethod, string? RootCause,
    int? ResponsiblePersonnelId, DateTime? DueDate,
    bool EffectivenessVerified, int? VerifiedByPersonnelId, DateTime? VerifiedAt, string? EffectivenessNote,
    IReadOnlyList<SaveCapaActionRequest> Actions,
    IReadOnlyList<SaveCapaRootCauseItemRequest> RootCauseItems);

public sealed record SaveCapaActionRequest(
    int Id, byte ActionType, string Description,
    int? ResponsiblePersonnelId, DateTime? DueDate,
    byte Status, DateTime? CompletedAt, int OrderNo);

/// <summary>Boş Text satırı Save'de elenir (CapaAction Description deseniyle aynı — anlamsız
/// satır sessizce yutulmaz, filtrelenir).</summary>
public sealed record SaveCapaRootCauseItemRequest(
    int Id, byte Method, byte? Category, int Sequence, string Text);

/// <summary>Id = DocumentId (Capa'nın companion anahtarı QualityInspection deseniyle aynı).</summary>
public sealed record ChangeCapaStatusRequest(int Id, byte NewStatus);

public sealed record CapaPersonnelOption(int Id, string Name);

/// <summary>Kaynak kayıt arama sonucu (muayene vb.) — DÖF formu "Kaynak" seçici.</summary>
public sealed record CapaSourceLookupItem(string SourceKind, int SourceId, string Label, DateTime? Date);

// ══════════════════════════════════════════════════════════════════
// DÖF KPI Panosu — salt-okunur agregasyon (JSON sözleşmesi frontend'e SABİT, camelCase serialize edilir)
// ══════════════════════════════════════════════════════════════════

public sealed record CapaKpiDto(
    int TotalCount, int OpenCount, int OverdueCount, int ClosedCount,
    int OpenedThisMonth, int ClosedThisMonth,
    double? AvgClosureDays,
    IReadOnlyList<CapaKpiBucket> ByStatus,
    IReadOnlyList<CapaKpiBucket> ByType,
    IReadOnlyList<CapaKpiBucket> BySeverity,
    IReadOnlyList<CapaKpiTrendPoint> MonthlyTrend,
    IReadOnlyList<CapaKpiResponsible> TopResponsible);

public sealed record CapaKpiBucket(byte Value, string Label, int Count);

/// <summary>Month = "yyyy-MM".</summary>
public sealed record CapaKpiTrendPoint(string Month, int Opened, int Closed);

public sealed record CapaKpiResponsible(int PersonnelId, string Name, int OpenCount);
