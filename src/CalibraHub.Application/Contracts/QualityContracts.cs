namespace CalibraHub.Application.Contracts;

// ══════════════════════════════════════════════════════════════════
// Hata Kodu Kataloğu
// ══════════════════════════════════════════════════════════════════

public sealed record QualityDefectCodeDto(
    int Id, byte Category, string CategoryLabel, string Code, string Name,
    string? Description, string? ColorHex, int SortOrder, bool IsActive);

/// <summary>Hata kodu kaydetme. Kod UI'dan alınmaz — isimden türetilir (K6).</summary>
public sealed record SaveQualityDefectCodeRequest(
    int? Id, byte Category, string Name, string? Description, string? ColorHex, int SortOrder, bool IsActive = true);

public sealed record QualityDefectCodeOption(int Id, string Name, string? ColorHex);

// ══════════════════════════════════════════════════════════════════
// Muayene Planı
// ══════════════════════════════════════════════════════════════════

public sealed record QualityInspectionPlanListItem(
    int Id, int? ItemId, string? ItemName, int? MaterialGroupId, string? MaterialGroupName,
    byte InspectionType, string InspectionTypeLabel, string Name, int LineCount, bool IsActive, DateTime Created);

public sealed record QualityInspectionPlanLineDto(
    int Id, string CharacteristicName, decimal? Nominal, decimal? LowerTol, decimal? UpperTol,
    int? UnitId, string? Method, string? GaugeName, bool IsNumeric, int OrderNo);

public sealed record QualityInspectionPlanDetail(
    int Id, int? ItemId, int? MaterialGroupId, byte InspectionType, string Name, bool IsActive,
    IReadOnlyCollection<QualityInspectionPlanLineDto> Lines);

public sealed record SaveQualityInspectionPlanRequest(
    int Id, int? ItemId, int? MaterialGroupId, byte InspectionType, string Name, bool IsActive,
    IReadOnlyList<QualityInspectionPlanLineDto> Lines);

// ══════════════════════════════════════════════════════════════════
// Muayene Kaydı
// ══════════════════════════════════════════════════════════════════

public sealed record QualityInspectionListItem(
    int DocumentId, string DocumentNumber, int? ItemId, string? ItemName,
    byte InspectionType, string InspectionTypeLabel, byte Status, string StatusLabel,
    byte? Verdict, string? VerdictLabel, byte? Disposition, string? DispositionLabel,
    DateTime? InspectedAt, DateTime Created);

public sealed record QualityInspectionLineDto(
    int Id, int? PlanLineId, string CharacteristicName, decimal? Nominal, decimal? LowerTol, decimal? UpperTol,
    decimal? Measured, bool IsNumeric, byte Result, int? DefectCodeId, string? DefectCodeName, int OrderNo, string? Notes);

public sealed record QualityInspectionDetail(
    int DocumentId, string DocumentNumber, int? PlanId, int? ItemId, byte InspectionType,
    byte Status, byte? Verdict, byte? Disposition, string? SourceKind, int? SourceId,
    decimal? Quantity, int? InspectedByPersonnelId, DateTime? InspectedAt, string? Notes,
    IReadOnlyCollection<QualityInspectionLineDto> Lines);

/// <summary>Muayene kaydetme. Id=0 (DocumentId) yeni; Verdict server'da ölçümlerden hesaplanır.</summary>
public sealed record SaveQualityInspectionRequest(
    int Id, int? PlanId, int? ItemId, byte InspectionType, string? SourceKind, int? SourceId,
    decimal? Quantity, int? InspectedByPersonnelId, DateTime? InspectedAt, string? Notes,
    IReadOnlyList<SaveQualityInspectionLine> Lines);

public sealed record SaveQualityInspectionLine(
    int Id, int? PlanLineId, string CharacteristicName, decimal? Nominal, decimal? LowerTol, decimal? UpperTol,
    decimal? Measured, bool IsNumeric, byte Result, int? DefectCodeId, int OrderNo, string? Notes);

public sealed record ChangeInspectionStatusRequest(int DocumentId, byte NewStatus);
public sealed record SetInspectionDispositionRequest(int DocumentId, byte Disposition);

/// <summary>Malzeme + tip için tanımlı plan (muayene açılışında satır türetme). Yoksa null.</summary>
public sealed record QualityPlanLookup(int PlanId, string PlanName, IReadOnlyCollection<QualityInspectionPlanLineDto> Lines);
