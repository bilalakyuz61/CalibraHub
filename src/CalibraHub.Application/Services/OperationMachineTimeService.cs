using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class OperationMachineTimeService : IOperationMachineTimeService
{
    private readonly IOperationMachineTimeRepository _repo;

    public OperationMachineTimeService(IOperationMachineTimeRepository repo) => _repo = repo;

    public Task<IReadOnlyCollection<OperationMachineTimeDto>> ListByOperationAsync(int operationId, CancellationToken ct)
        => _repo.ListByOperationAsync(operationId, ct);

    public Task<int> SaveAsync(SaveOperationMachineTimeRequest req, CancellationToken ct)
    {
        if (req.OperationId <= 0) throw new ArgumentException("Operasyon zorunlu.");
        if (req.MachineId <= 0) throw new ArgumentException("Makine zorunlu.");
        if (req.Quantity <= 0) throw new ArgumentException("Miktar 0'dan büyük olmalı.");
        if (req.DurationPerUnit < 0) throw new ArgumentException("Süre negatif olamaz.");

        var entity = new OperationMachineTime
        {
            Id = req.Id,
            OperationId = req.OperationId,
            MachineId = req.MachineId,
            ItemId = req.ItemId,
            Quantity = req.Quantity,
            DurationPerUnit = req.DurationPerUnit,
            DurationUnit = req.DurationUnit,
            IsActive = req.IsActive,
        };
        return _repo.SaveAsync(entity, ct);
    }

    public Task DeleteAsync(int id, CancellationToken ct) => _repo.DeleteAsync(id, ct);
}
