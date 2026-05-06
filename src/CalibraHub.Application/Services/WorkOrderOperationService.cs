using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class WorkOrderOperationService : IWorkOrderOperationService
{
    private readonly IWorkOrderOperationRepository _repo;

    public WorkOrderOperationService(IWorkOrderOperationRepository repo) => _repo = repo;

    public Task<IReadOnlyCollection<WorkOrderOperationDto>> GetByWorkOrderAsync(int workOrderId, CancellationToken ct)
        => _repo.GetByWorkOrderAsync(workOrderId, ct);

    public Task<IReadOnlyCollection<WorkOrderOperationDto>> GetQueueByMachineAsync(int machineId, CancellationToken ct)
        => _repo.GetQueueByMachineAsync(machineId, ct);

    public Task<WorkOrderOperationDto?> GetAsync(int id, CancellationToken ct) => _repo.GetAsync(id, ct);

    public Task<int> SaveAsync(SaveWorkOrderOperationRequest req, CancellationToken ct)
    {
        if (req.WorkOrderId <= 0) throw new ArgumentException("İş emri zorunlu.");
        if (req.OperationId <= 0) throw new ArgumentException("Operasyon zorunlu.");
        if (req.Sequence < 0) throw new ArgumentException("Sıra negatif olamaz.");
        var entity = new WorkOrderOperation
        {
            Id = req.Id,
            WorkOrderId = req.WorkOrderId,
            Sequence = req.Sequence,
            OperationId = req.OperationId,
            MachineId = req.MachineId,
            PlannedDuration = req.PlannedDuration,
            DurationUnit = req.DurationUnit,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
        };
        return _repo.SaveAsync(entity, ct);
    }

    public Task DeleteAsync(int id, CancellationToken ct) => _repo.DeleteAsync(id, ct);

    public Task ExplodeFromRoutingAsync(int workOrderId, int routingId, CancellationToken ct)
        => _repo.ExplodeFromRoutingAsync(workOrderId, routingId, ct);

    public Task StartAsync(StartOperationRequest req, CancellationToken ct)
    {
        if (req.WorkOrderOperationId <= 0) throw new ArgumentException("Operasyon kaydı zorunlu.");
        if (req.OperatorPersonnelId <= 0) throw new ArgumentException("Operatör (Personnel) zorunlu.");
        return _repo.StartAsync(req.WorkOrderOperationId, req.OperatorPersonnelId, ct);
    }

    public Task PartialCompleteAsync(PartialCompleteOperationRequest req, CancellationToken ct)
    {
        if (req.WorkOrderOperationId <= 0) throw new ArgumentException("Operasyon kaydı zorunlu.");
        if (req.OperatorPersonnelId <= 0) throw new ArgumentException("Operatör (Personnel) zorunlu.");
        if (req.Quantity <= 0) throw new ArgumentException("Miktar 0'dan büyük olmalı.");
        return _repo.PartialCompleteAsync(req.WorkOrderOperationId, req.OperatorPersonnelId, req.Quantity, req.ScrapQuantity, ct);
    }

    public Task CompleteAsync(CompleteOperationRequest req, CancellationToken ct)
    {
        if (req.WorkOrderOperationId <= 0) throw new ArgumentException("Operasyon kaydı zorunlu.");
        if (req.OperatorPersonnelId <= 0) throw new ArgumentException("Operatör (Personnel) zorunlu.");
        return _repo.CompleteAsync(req.WorkOrderOperationId, req.OperatorPersonnelId, req.FinalQuantity, ct);
    }
}
