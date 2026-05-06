using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IOperationMachineTimeRepository
{
    /// <summary>Operasyona göre tüm makine süre kayıtları (sözlük tab'ı).</summary>
    Task<IReadOnlyCollection<OperationMachineTimeDto>> ListByOperationAsync(int operationId, CancellationToken ct);
    Task<int> SaveAsync(OperationMachineTime entity, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
}
