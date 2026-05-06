using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

/// <summary>Tek satirlik liste DTO — board/grid icin.</summary>
public sealed record WorkOrderListItemDto(
    int Id,
    string OrderNumber,
    DateTime OrderDate,
    int ItemId,
    string? ItemCode,
    string? ItemName,
    int? ConfigId,
    decimal PlannedQuantity,
    decimal ProducedQuantity,
    int? UnitId,
    string? UnitCode,
    WorkOrderStatus Status,
    WorkOrderPriority Priority,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    Guid? AssignedUserId,
    string? AssignedUserName,
    int RevisionNo);

public sealed record WorkOrderSourceDto(
    int Id,
    int WorkOrderId,
    int SourceDocumentId,
    string? SourceDocumentNumber,
    int SourceLineId,
    decimal AllocatedQuantity);

/// <summary>Detay (edit) DTO — header tum alanlari + source listesi.</summary>
public sealed record WorkOrderDto(
    int Id,
    int CompanyId,
    string OrderNumber,
    DateTime OrderDate,
    int ItemId,
    string? ItemCode,
    string? ItemName,
    int? ConfigId,
    decimal PlannedQuantity,
    decimal ProducedQuantity,
    decimal ScrapQuantity,
    int? UnitId,
    string? UnitCode,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    DateTime? ActualStartDate,
    DateTime? ActualEndDate,
    WorkOrderStatus Status,
    WorkOrderPriority Priority,
    Guid? AssignedUserId,
    string? AssignedUserName,
    int? WarehouseLocationId,
    string? WarehouseLocationCode,
    int RevisionNo,
    int? ParentWorkOrderId,
    int? RevisedFromId,
    int? RoutingId,
    string? RoutingCode,
    string? RoutingName,
    string? Notes,
    DateTime Created,
    DateTime? Updated,
    IReadOnlyCollection<WorkOrderSourceDto> Sources);

public sealed record CreateWorkOrderRequest(
    int ItemId,
    int? ConfigId,
    decimal PlannedQuantity,
    int? UnitId,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    WorkOrderPriority Priority,
    Guid? AssignedUserId,
    int? WarehouseLocationId,
    int? RoutingId,
    string? Notes);

public sealed record UpdateWorkOrderRequest(
    decimal PlannedQuantity,
    int? UnitId,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    WorkOrderPriority Priority,
    Guid? AssignedUserId,
    int? WarehouseLocationId,
    int? RoutingId,
    string? Notes);

public sealed record ChangeWorkOrderStatusRequest(int WorkOrderId, WorkOrderStatus NewStatus);

/// <summary>Sales order satirindan is emri olusturma — bolme + toplama destekli.</summary>
public sealed record CreateWorkOrderFromSalesLineRequest(
    int SourceDocumentId,
    int SourceLineId,
    decimal Quantity,
    /// <summary>Doluysa mevcut emire AllocatedQuantity ekler (toplama). NULL ise yeni emir acar.</summary>
    int? TargetWorkOrderId);
