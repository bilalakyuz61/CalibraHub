using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using System.Linq;

namespace CalibraHub.Application.Services;

public sealed class WorkOrderOperationService : IWorkOrderOperationService
{
    private readonly IWorkOrderOperationRepository _repo;
    private readonly IWorkOrderRepository _workOrders;
    private readonly ILotRepository _lots;

    public WorkOrderOperationService(IWorkOrderOperationRepository repo, IWorkOrderRepository workOrders, ILotRepository lots)
    {
        _repo = repo;
        _workOrders = workOrders;
        _lots = lots;
    }

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

        // 2026-07-02: op her durumda lazim — hem upstream cap kontrolu hem de "son operasyon mu"
        // tespiti icin (son operasyon tamamlaninca mamul girisi DocumentLine'a append edilir).
        var op = await _repo.GetAsync(req.WorkOrderOperationId, ct)
            ?? throw new InvalidOperationException("Operasyon bulunamadı.");

        // 2026-05-22: Final miktar verildiyse upstream cap kontrolü. FinalQuantity null ise
        // mevcut ProducedQuantity korunur (sadece status = Completed olur), cap zaten Partial'da
        // tutulduğundan ek kontrol gerekmez.
        if (req.FinalQuantity.HasValue && req.FinalQuantity.Value > op.UpstreamCap)
            throw new InvalidOperationException(
                $"Final miktar upstream limitini aşıyor: {req.FinalQuantity.Value:N2} > {op.UpstreamCap:N2}");

        // Bu, iş emrindeki en yüksek Sequence'lı operasyon mu? Öyleyse mamul girişi yazılır
        // (üretimin son adımı = stoğa giren mamul). Ara operasyonlar stok hareketi yaratmaz.
        DocumentLine? stockLine = null;
        var allOps = await _repo.GetByWorkOrderAsync(op.WorkOrderId, ct);
        var isLastOperation = allOps.Count > 0 && op.Sequence == allOps.Max(o => o.Sequence);
        if (isLastOperation)
        {
            var wo = await _workOrders.GetAsync(op.WorkOrderId, ct)
                ?? throw new InvalidOperationException("İş emri bulunamadı.");
            var finalQty = req.FinalQuantity ?? op.ProducedQuantity;
            if (finalQty > 0)
            {
                // Lot-takipli mamulde otomatik lot = iş emri numarası (üretim partisi).
                // Lot soy ağacının temeli: mamul lotu iş emrine, iş emri sarflara bağlanır.
                int? lotId = null;
                string? lotNo = null;
                var tracking = await _lots.GetItemTrackingTypeAsync(wo.ItemId, ct);
                if (string.Equals(tracking, "Lot", StringComparison.OrdinalIgnoreCase))
                {
                    lotNo = wo.OrderNumber;
                    lotId = await _lots.GetOrCreateAsync(wo.ItemId, lotNo, null, ct);
                }

                stockLine = new DocumentLine
                {
                    DocumentId = wo.DocumentId,
                    ItemId = wo.ItemId,
                    CombinationId = wo.ConfigId,
                    UnitId = wo.UnitId,
                    Quantity = finalQty,
                    LocationId = wo.WarehouseLocationId,
                    MovementType = (byte)StockMovementType.Receipt,
                    LotId = lotId,
                    LotNo = lotNo,
                    Notes = $"İş Emri #{wo.OrderNumber} — üretim tamamlama",
                };
            }
        }

        await _repo.CompleteAsync(req.WorkOrderOperationId, req.OperatorPersonnelId, req.FinalQuantity, stockLine, ct);
    }
}
