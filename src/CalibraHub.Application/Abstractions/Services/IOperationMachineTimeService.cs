using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IOperationMachineTimeService
{
    Task<IReadOnlyCollection<OperationMachineTimeDto>> ListByOperationAsync(int operationId, int? routingId, CancellationToken ct);
    Task<int> SaveAsync(SaveOperationMachineTimeRequest request, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>
    /// Makine Planlama süre resolver (Faz 1, 2026-08-04) — en spesifik eşleşme zinciri:
    /// OperationMachineTime (op×makine×ürün×rota; DurationPerUnit/Quantity × quantity) →
    /// RoutingOperation.OverrideDuration (flat, ölçeklenmez) → Operation.StandardDuration (flat).
    /// Sonuç dakika cinsinden; hiçbir kaynak yoksa null.
    /// </summary>
    Task<decimal?> ResolveDurationMinutesAsync(
        int operationId, int? machineId, int? itemId, int? routingId, decimal quantity, CancellationToken ct);

    /// <summary>
    /// Makine Planlama süre resolver Faz 2 (2026-08-05) — en spesifik <c>OperationMachineTime</c>
    /// eşleşmesinin <c>SetupDuration</c> alanı, satırın kendi <c>DurationUnit</c>'i ile dakikaya
    /// normalize edilir (miktarla ÖLÇEKLENMEZ — setup sabit hazırlık süresidir). Eşleşme yoksa
    /// veya <c>SetupDuration</c> boş/0 ise null.
    /// </summary>
    Task<decimal?> ResolveSetupMinutesAsync(
        int operationId, int? machineId, int? itemId, int? routingId, CancellationToken ct);
}
