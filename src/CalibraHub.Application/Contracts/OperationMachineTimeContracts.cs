using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

public sealed record OperationMachineTimeDto(
    int Id,
    int OperationId,
    string? OperationCode,
    string? OperationName,
    int? RoutingId,
    int? MachineId,
    string? Code,
    string? Name,
    int? MachineGroupId,
    string? MachineGroupCode,
    int? ItemId,
    string? ItemCode,
    string? ItemName,
    int? ItemGroupId,
    string? ItemGroupCode,
    int? UnitId,
    string? UnitCode,
    string? UnitName,
    decimal Quantity,
    decimal DurationPerUnit,
    DurationUnit DurationUnit,
    bool IsActive,
    DateTime Created,
    DateTime? Updated);

public sealed record SaveOperationMachineTimeRequest(
    int Id,
    int OperationId,
    int? RoutingId,
    int? MachineId,
    int? MachineGroupId,
    int? ItemId,
    int? ItemGroupId,
    int? UnitId,
    decimal Quantity,
    decimal DurationPerUnit,
    DurationUnit DurationUnit,
    bool IsActive);
