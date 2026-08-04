namespace CalibraHub.Application.Contracts;

/// <summary>
/// Makine Planlama (Üretim Çizelgeleme) — Faz 1 Manuel (2026-08-04). API sözleşmesi KİLİTLİ
/// (bkz. plan glittery-finding-comet.md) — frontend bu DTO'lara göre yazıldı, alan adı/tipini
/// değiştirme. BlockType/Status kasıtlı olarak <c>byte</c> (domain enum DEĞİL) — global
/// JsonStringEnumConverter (Program.cs) enum tipini string'e çevirir, ama sözleşme integer
/// bekliyor (1=Production/2=Setup/3=Maintenance/4=Down; 1=Planned/2=Locked/3=Confirmed).
/// </summary>
public sealed record ScheduleMachineDto(
    int Id,
    string Code,
    string? Name,
    decimal? HourlyCapacity);

public sealed record ScheduleBlockDto(
    int Id,
    int MachineId,
    int? WorkOrderOperationId,
    DateTime StartUtc,
    DateTime EndUtc,
    byte BlockType,
    byte Status,
    string? WorkOrderNo,
    string? ItemName,
    string? OperationName,
    decimal? Quantity,
    string? Notes);

public sealed record UnplannedOperationDto(
    int WorkOrderOperationId,
    int WorkOrderId,
    string? WorkOrderNo,
    string? ItemName,
    string? OperationName,
    int Sequence,
    int? MachineId,
    decimal? PlannedQuantity,
    decimal? EstimatedMinutes);

public sealed record MachineScheduleDataDto(
    IReadOnlyList<MachineDto> Machines,
    IReadOnlyList<ScheduleBlockDto> Blocks,
    IReadOnlyList<UnplannedOperationDto> Unplanned);

public sealed record SaveScheduleBlockRequest(
    int Id,
    int MachineId,
    int? WorkOrderOperationId,
    DateTime StartUtc,
    DateTime EndUtc,
    byte BlockType,
    byte Status,
    string? Notes);

public sealed record ScheduleBlockConflictDto(
    int Id,
    string? WorkOrderNo,
    DateTime StartUtc,
    DateTime EndUtc);

public sealed record SaveScheduleBlockResult(
    bool Ok,
    int Id,
    IReadOnlyList<ScheduleBlockConflictDto> Conflicts);
