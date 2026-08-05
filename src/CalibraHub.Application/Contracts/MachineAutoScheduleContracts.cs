namespace CalibraHub.Application.Contracts;

/// <summary>
/// Makine Planlama Faz 3 (2026-08-05) — otomatik çizelgeleme önerisi (forward, sonlu-kapasite
/// motor). API sözleşmesi KİLİTLİ (bkz. backend raporu) — frontend "Otomatik Yerleştir" akışı
/// bu DTO'lara göre yazılır, alan adı/tipini değiştirme. BlockType byte (1=Production/2=Setup) —
/// Faz 1/2 ile aynı konvansiyon (bkz. <c>MachineScheduleContracts.cs</c> XML doc'u). Tüm zaman
/// alanları UTC.
/// </summary>
public sealed record AutoScheduleCandidateWorkOrderDto(
    int WorkOrderId,
    string? WorkOrderNo,
    string? ItemName,
    byte Priority,
    DateTime? PlannedEndDate,
    int UnplannedOpCount,
    decimal TotalMinutes);

/// <summary><c>ScenarioId</c> Vardiya Senaryoları (2026-08-05) eklentisi — null ise motor
/// varsayılan senaryoyu kullanır (bkz. <c>IMachineCalendarRepository.ListWorkWindowsAsync</c>).
/// Sona eklendi, positional record — mevcut client payload'ları (alan yok) kırılmaz.</summary>
public sealed record AutoScheduleRequest(
    IReadOnlyList<int> IncludedWorkOrderIds,
    DateTime FromUtc,
    int? ScenarioId = null);

public sealed record AutoScheduleProposalDto(
    string TempId,
    int MachineId,
    string? MachineName,
    int WorkOrderOperationId,
    string? WorkOrderNo,
    string? ItemName,
    string? OperationName,
    byte BlockType,
    DateTime StartUtc,
    DateTime EndUtc,
    int SegmentIndex,
    int SegmentCount);

public sealed record AutoScheduleUnplaceableDto(
    int WorkOrderOperationId,
    string? WorkOrderNo,
    string? OperationName,
    string Reason);

public sealed record AutoSchedulePreviewResultDto(
    IReadOnlyList<AutoScheduleProposalDto> Proposals,
    IReadOnlyList<AutoScheduleUnplaceableDto> Unplaceable);

public sealed record AutoScheduleApplyResultDto(
    int CreatedCount,
    int UnplaceableCount);

// ── Repository transfer DTO'ları (motor girdi/çıktı — public API sözleşmesi DEĞİL) ─────────

/// <summary>Bir iş emri operasyonunun motor girdisi — planlanmamış (aktif bloğu olmayan) operasyonlar.</summary>
public sealed record AutoScheduleOperationInputDto(
    int WorkOrderOperationId,
    int WorkOrderId,
    string? WorkOrderNo,
    string? ItemName,
    string? OperationName,
    int Sequence,
    int? MachineId,
    int OperationId,
    int ItemId,
    int? RoutingId,
    decimal? PlannedQuantity,
    decimal? PlannedDuration,
    byte DurationUnit,
    byte Priority,
    DateTime? PlannedEndDate);

/// <summary>Bir makinede mevcut (aktif) meşgul zaman aralığı — motorun kapasite kısıtı girdisi.</summary>
public sealed record OccupiedBlockDto(int MachineId, DateTime StartUtc, DateTime EndUtc);

/// <summary>Apply'da kalıcı hale getirilecek tek bir zaman segmenti.</summary>
public sealed record PlannedSegmentDto(int MachineId, DateTime StartUtc, DateTime EndUtc, byte BlockType);

/// <summary>Bir operasyona ait, motor tarafından hesaplanmış kronolojik segment listesi (Apply girdisi).
/// Segments sırası kronolojiktir: varsa Setup segmentleri ÖNCE, Production segmentleri SONRA gelir
/// (bkz. <c>MachineAutoScheduleService</c> — setup+üretim TEK yerleştirme çağrısıyla hesaplanır,
/// ilk N dakikası Setup'a bölünür).</summary>
public sealed record PlannedOperationBlocksDto(
    int WorkOrderOperationId,
    IReadOnlyList<PlannedSegmentDto> Segments);
