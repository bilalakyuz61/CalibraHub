using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

public sealed record OperationDto(
    int Id,
    string Code,
    string Name,
    string? Description,
    decimal? StandardDuration,
    DurationUnit DurationUnit,
    decimal? HourlyRate,
    int SortOrder,
    bool IsActive,
    DateTime Created,
    DateTime? Updated);

public sealed record SaveOperationRequest(
    int Id,
    string Code,
    string Name,
    string? Description,
    decimal? StandardDuration,
    DurationUnit DurationUnit,
    decimal? HourlyRate,
    int SortOrder,
    bool IsActive);
