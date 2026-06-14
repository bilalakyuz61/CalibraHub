using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

public sealed record RoutingDto(
    int Id,
    string Code,
    string Name,
    int? ItemId,
    string? ItemCode,
    string? ItemName,
    int? ConfigId,
    string? Description,
    bool IsActive,
    int OperationCount,
    DateTime Created,
    DateTime? Updated);

public sealed record RoutingOperationDto(
    int Id,
    int RoutingId,
    int Sequence,
    int OperationId,
    string? OperationCode,
    string? OperationName,
    int? MachineId,
    string? Code,
    string? Name,
    decimal? OverrideDuration,
    DurationUnit DurationUnit,
    string? Notes);

public sealed record SaveRoutingRequest(
    int Id,
    string Code,
    string Name,
    int? ItemId,
    int? ConfigId,
    string? Description,
    bool IsActive,
    IReadOnlyList<SaveRoutingOperationLine>? Operations);

public sealed record SaveRoutingOperationLine(
    int Sequence,
    int OperationId,
    int? MachineId,
    decimal? OverrideDuration,
    DurationUnit DurationUnit,
    string? Notes);

public sealed record RoutingItemMapDto(
    int Id,
    int RoutingId,
    int ItemId,
    string? ItemCode,
    string? ItemName,
    int? ConfigId,
    string? CombinationCode,
    string? CombinationName);

public sealed record RoutingWithOpsDto(
    RoutingDto Header,
    IReadOnlyList<RoutingOperationDto> Operations);
