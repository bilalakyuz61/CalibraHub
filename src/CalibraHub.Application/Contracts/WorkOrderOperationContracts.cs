using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

public sealed record WorkOrderOperationDto(
    int Id,
    int WorkOrderId,
    int Sequence,
    int OperationId,
    string? OperationCode,
    string? OperationName,
    int? MachineId,
    string? MachineCode,
    string? MachineName,
    decimal? PlannedDuration,
    DurationUnit DurationUnit,
    decimal? ActualDuration,
    decimal ProducedQuantity,
    decimal ScrapQuantity,
    WorkOrderOperationStatus Status,
    int? StartedByPersonnelId,
    string? StartedByPersonnelName,
    DateTime? StartedAt,
    int? CompletedByPersonnelId,
    string? CompletedByPersonnelName,
    DateTime? CompletedAt,
    string? Notes);

public sealed record SaveWorkOrderOperationRequest(
    int Id,
    int WorkOrderId,
    int Sequence,
    int OperationId,
    int? MachineId,
    decimal? PlannedDuration,
    DurationUnit DurationUnit,
    string? Notes);

/// <summary>Shop-floor: operasyon başlat. OperatorPersonnelId = Personnel.Id (PIN/NFC ile auth edilen kayıt).</summary>
public sealed record StartOperationRequest(int WorkOrderOperationId, int OperatorPersonnelId);

/// <summary>Shop-floor: operasyonu kısmi bitir (miktar gir).</summary>
public sealed record PartialCompleteOperationRequest(
    int WorkOrderOperationId,
    int OperatorPersonnelId,
    decimal Quantity,
    decimal? ScrapQuantity);

/// <summary>Shop-floor: operasyonu tam bitir (final miktar).</summary>
public sealed record CompleteOperationRequest(
    int WorkOrderOperationId,
    int OperatorPersonnelId,
    decimal? FinalQuantity);
