using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

public sealed record OperationMachineTimeDto(
    int Id,
    int OperationId,
    string? OperationCode,
    string? OperationName,
    int MachineId,
    string? MachineCode,
    string? MachineName,
    int? ItemId,
    string? ItemCode,
    string? ItemName,
    decimal Quantity,
    decimal DurationPerUnit,
    DurationUnit DurationUnit,
    bool IsActive,
    DateTime Created,
    DateTime? Updated);

public sealed record SaveOperationMachineTimeRequest(
    int Id,
    int OperationId,
    int MachineId,
    int? ItemId,
    decimal Quantity,
    decimal DurationPerUnit,
    DurationUnit DurationUnit,
    bool IsActive);
