using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IOperationRepository
{
    Task<IReadOnlyCollection<Operation>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<Operation?> GetAsync(int id, CancellationToken ct);
    Task<int> UpsertAsync(Operation entity, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>
    /// Silme öncesi kullanım kontrolü — operasyonu referans veren üç tabloda kaç kayıt
    /// var (bkz. FK_RoutingOperation_Operation / FK_WorkOrderOperation_Operation /
    /// FK_OpMachineTime_Operation). Hepsi 0 ise operasyon kullanımda değildir.
    /// </summary>
    Task<(int RoutingCount, int WorkOrderCount, int MachineTimeCount)> GetUsageCountsAsync(int id, CancellationToken ct);
}
