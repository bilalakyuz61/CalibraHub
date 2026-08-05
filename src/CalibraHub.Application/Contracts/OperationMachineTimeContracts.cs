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
    DateTime? Updated,
    // Makine Planlama Faz 2 (2026-08-05) — operasyon setup (hazırlık) süresi. Satırın kendi
    // DurationUnit'i ile yorumlanır (ayrı birim yok). Yerleşince otomatik "Hazırlık" bloğu
    // (BlockType=2) üretilir — bkz. SqlMachineScheduleRepository.SaveBlockAsync.
    decimal? SetupDuration = null);

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
    bool IsActive,
    decimal? SetupDuration = null);

/// <summary>
/// Makine Planlama süre resolver (Faz 1, 2026-08-04) — en spesifik <c>OperationMachineTime</c> eşleşmesi.
/// YAGNI basitleştirmesi: yalnız <c>MachineId</c>/<c>ItemId</c>/<c>RoutingId</c> tam eşleşme aranır;
/// <c>MachineGroupId</c>/<c>ItemGroupId</c> (makine/ürün grubu) eşleştirmesi KAPSAM DIŞI bırakıldı
/// (Machine entity'sinde grup referansı yok — CardGroup üyelik tablosu ayrı bir keşif gerektirir).
/// </summary>
public sealed record OperationMachineTimeMatchDto(
    int Id,
    int? MachineId,
    int? ItemId,
    int? RoutingId,
    decimal Quantity,
    decimal DurationPerUnit,
    DurationUnit DurationUnit,
    // Faz 2 (2026-08-05) — setup resolver bu alanı kullanır (bkz. IOperationMachineTimeService.ResolveSetupMinutesAsync).
    decimal? SetupDuration = null);
