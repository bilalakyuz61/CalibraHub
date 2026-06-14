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

    public async Task StartAsync(StartOperationRequest req, CancellationToken ct)
    {
        if (req.WorkOrderOperationId <= 0) throw new ArgumentException("Operasyon kaydı zorunlu.");
        if (req.OperatorPersonnelId <= 0) throw new ArgumentException("Operatör (Personnel) zorunlu.");
        // 2026-05-22: Upstream cap — önceki operasyonların net üretimi 0 ise
        // bu op henüz başlayamaz (downstream sequence kuralı). İlk op'ta cap=PlannedQty
        // (UpstreamCap>0 garantili) → ilk op her zaman başlatılabilir.
        var op = await _repo.GetAsync(req.WorkOrderOperationId, ct);
        if (op is null) throw new InvalidOperationException("Operasyon bulunamadı.");
        if (op.UpstreamCap <= 0)
            throw new InvalidOperationException(
                $"Önceki operasyon henüz üretim yapmadı (Sıra {op.Sequence}). " +
                "Bu operasyonu başlatmadan önce upstream operasyonun üretimini girin.");
        await _repo.StartAsync(req.WorkOrderOperationId, req.OperatorPersonnelId, ct);
    }

    public async Task PartialCompleteAsync(PartialCompleteOperationRequest req, CancellationToken ct)
    {
        if (req.WorkOrderOperationId <= 0) throw new ArgumentException("Operasyon kaydı zorunlu.");
        if (req.OperatorPersonnelId <= 0) throw new ArgumentException("Operatör (Personnel) zorunlu.");
        if (req.Quantity <= 0) throw new ArgumentException("Miktar 0'dan büyük olmalı.");
        // 2026-05-22: Upstream cap — toplam (mevcut + yeni miktar) upstream net üretimini aşamaz.
        var op = await _repo.GetAsync(req.WorkOrderOperationId, ct);
        if (op is null) throw new InvalidOperationException("Operasyon bulunamadı.");
        var newTotal = op.ProducedQuantity + req.Quantity;
        if (newTotal > op.UpstreamCap)
            throw new InvalidOperationException(
                $"Üretim limiti aşıldı. Önceki operasyonlardan gelen miktar: {op.UpstreamCap:N2}. " +
                $"Bu op'ta mevcut üretim: {op.ProducedQuantity:N2}. " +
                $"Girebileceğiniz en fazla: {(op.UpstreamCap - op.ProducedQuantity):N2}.");
        await _repo.PartialCompleteAsync(req.WorkOrderOperationId, req.OperatorPersonnelId, req.Quantity, req.ScrapQuantity, ct);
    }

    public async Task CompleteAsync(CompleteOperationRequest req, CancellationToken ct)
    {
        if (req.WorkOrderOperationId <= 0) throw new ArgumentException("Operasyon kaydı zorunlu.");
        if (req.OperatorPersonnelId <= 0) throw new ArgumentException("Operatör (Personnel) zorunlu.");
        // 2026-05-22: Final miktar verildiyse upstream cap kontrolü. FinalQuantity null ise
        // mevcut ProducedQuantity korunur (sadece status = Completed olur), cap zaten Partial'da
        // tutulduğundan ek kontrol gerekmez.
        if (req.FinalQuantity.HasValue)
        {
            var op = await _repo.GetAsync(req.WorkOrderOperationId, ct);
            if (op is null) throw new InvalidOperationException("Operasyon bulunamadı.");
            if (req.FinalQuantity.Value > op.UpstreamCap)
                throw new InvalidOperationException(
                    $"Final miktar upstream limitini aşıyor: {req.FinalQuantity.Value:N2} > {op.UpstreamCap:N2}");
        }
        await _repo.CompleteAsync(req.WorkOrderOperationId, req.OperatorPersonnelId, req.FinalQuantity, ct);
    }
}
