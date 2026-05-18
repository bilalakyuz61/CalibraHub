using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

/// <summary>
/// Uretim is emri uygulama servisi. Numara uretimi (NumeratorService),
/// default parametreler (CompanyParameterService), source baglama (sales line)
/// ve revize akisini yonetir.
/// </summary>
public sealed class WorkOrderService : IWorkOrderService
{
    private const string FormCode = "WORK_ORDER";
    // Format: WO-26-000000001 (yıl son 2 hane + 9 haneli dolgulu sayaç).
    // Şirket parametre ekranında {yyyy}, {yy}, {MM}, {dd}, {seq:N} placeholder'ları
    // ile özelleştirilebilir; bu sabit yalnızca parametre boş olduğunda fallback.
    private const string DefaultNumberMask = "WO-{yy}-{seq:9}";

    private readonly IWorkOrderRepository _workOrders;
    private readonly INumeratorService _numerator;
    private readonly ICompanyParameterService _parameters;
    private readonly IDocumentRepository _documents;
    private readonly IWorkOrderOperationRepository _workOrderOperations;
    private readonly IWorkOrderComponentRepository _workOrderComponents;
    private readonly ILogisticsConfigurationRepository _logisticsConfig;

    public WorkOrderService(
        IWorkOrderRepository workOrders,
        INumeratorService numerator,
        ICompanyParameterService parameters,
        IDocumentRepository documents,
        IWorkOrderOperationRepository workOrderOperations,
        IWorkOrderComponentRepository workOrderComponents,
        ILogisticsConfigurationRepository logisticsConfig)
    {
        _workOrders = workOrders;
        _numerator = numerator;
        _parameters = parameters;
        _documents = documents;
        _workOrderOperations = workOrderOperations;
        _workOrderComponents = workOrderComponents;
        _logisticsConfig = logisticsConfig;
    }

    public Task<IReadOnlyCollection<WorkOrderListItemDto>> ListAsync(WorkOrderStatus? status, CancellationToken ct)
        => _workOrders.ListAsync(status, ct);

    public Task<WorkOrderDto?> GetAsync(int id, CancellationToken ct)
        => _workOrders.GetAsync(id, ct);

    public async Task<int> CreateAsync(CreateWorkOrderRequest request, CancellationToken ct)
    {
        if (request.PlannedQuantity <= 0)
            throw new ArgumentException("Planlanan miktar 0'dan buyuk olmali.", nameof(request.PlannedQuantity));
        if (request.ItemId <= 0)
            throw new ArgumentException("Mamul (ItemId) zorunlu.", nameof(request.ItemId));

        var orderNumber = await _numerator.GetNextNumberAsync("WORK_ORDER", FormCode, DefaultNumberMask, ct);

        var defaultLocationId = request.WarehouseLocationId
            ?? await _parameters.GetIntAsync(FormCode, "DefaultLocationId", ct);
        Guid? defaultUserId = request.AssignedUserId;
        if (!defaultUserId.HasValue)
        {
            var rawUserId = await _parameters.GetStringAsync(FormCode, "DefaultAssignedUserId", ct);
            if (Guid.TryParse(rawUserId, out var parsedUser)) defaultUserId = parsedUser;
        }
        var defaultPlanDays = await _parameters.GetIntAsync(FormCode, "DefaultPlanDays", ct) ?? 7;
        // Per-WO switch ("Üretime Planla") öncelikli — request.AutoRelease set ise onu kullan,
        // yoksa CompanyParameter fallback. Frontend default ON gönderir; user kapatırsa false.
        var autoRelease = request.AutoRelease
            ?? (await _parameters.GetBoolAsync(FormCode, "AutoRelease", ct) ?? false);

        var orderDate = DateTime.UtcNow;
        var plannedEnd = request.PlannedEndDate ?? orderDate.AddDays(defaultPlanDays);

        // Routing resolve: request'te verildiyse onu kullan, yoksa Item bazlı arama yap (Faz 3a default rota).
        var resolvedRoutingId = request.RoutingId
            ?? await _workOrders.FindRoutingForItemAsync(request.ItemId, request.ConfigId, ct);

        var entity = new WorkOrder
        {
            OrderNumber = orderNumber,
            OrderDate = orderDate,
            ItemId = request.ItemId,
            ConfigId = request.ConfigId,
            PlannedQuantity = request.PlannedQuantity,
            UnitId = request.UnitId,
            PlannedStartDate = request.PlannedStartDate ?? orderDate,
            PlannedEndDate = plannedEnd,
            Status = autoRelease ? WorkOrderStatus.Released : WorkOrderStatus.Planned,
            Priority = request.Priority,
            AssignedUserId = defaultUserId,
            WarehouseLocationId = defaultLocationId,
            RoutingId = resolvedRoutingId,
            DefaultMachineId = request.DefaultMachineId,
            AssignedPersonnelId = request.AssignedPersonnelId,
            Notes = request.Notes,
        };

        var newId = await _workOrders.CreateAsync(entity, ct);

        // AutoRelease ise routing varsa direkt operasyonları patlat.
        if (autoRelease && resolvedRoutingId.HasValue)
        {
            await _workOrderOperations.ExplodeFromRoutingAsync(newId, resolvedRoutingId.Value, ct);
        }

        return newId;
    }

    public Task UpdateAsync(int id, UpdateWorkOrderRequest request, CancellationToken ct)
    {
        if (request.PlannedQuantity <= 0)
            throw new ArgumentException("Planlanan miktar 0'dan buyuk olmali.", nameof(request.PlannedQuantity));
        return _workOrders.UpdateAsync(id, request, null, ct);
    }

    public async Task ChangeStatusAsync(int id, WorkOrderStatus newStatus, CancellationToken ct)
    {
        var current = await _workOrders.GetAsync(id, ct)
            ?? throw new InvalidOperationException("Is emri bulunamadi.");

        ValidateTransition(current.Status, newStatus);

        // Faz 3a — Released transition: rota → operasyon patlatma (transactional repo katmaninda).
        // Sadece Planned -> Released gecisinde calisir. Routing yoksa Item bazli aranir,
        // bulunamazsa hata firlatilir (Released icin rota zorunlu).
        if (newStatus == WorkOrderStatus.Released && current.Status != WorkOrderStatus.Released)
        {
            var routingId = current.RoutingId
                ?? await _workOrders.FindRoutingForItemAsync(current.ItemId, current.ConfigId, ct)
                ?? throw new InvalidOperationException(
                    "Bu mamul için tanımlı bir rota bulunamadı. Önce rota tanımlayın veya iş emrinin Rota alanını seçin.");

            // RoutingId emirde NULL idiyse persist et (idempotent: aynısını yazmak zararsız).
            if (current.RoutingId != routingId)
            {
                await _workOrders.SetRoutingIdAsync(id, routingId, ct);
            }

            // Operasyonları kopyala (idempotent — eski operasyonlar silinip yeniden açılır).
            await _workOrderOperations.ExplodeFromRoutingAsync(id, routingId, ct);
        }

        await _workOrders.ChangeStatusAsync(id, newStatus, null, ct);
    }

    public async Task<int> ReviseAsync(int id, CancellationToken ct)
    {
        var current = await _workOrders.GetAsync(id, ct)
            ?? throw new InvalidOperationException("Is emri bulunamadi.");

        if (current.Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled)
            throw new InvalidOperationException("Kapali veya iptal edilmis is emri revize edilemez.");

        return await _workOrders.CreateRevisionAsync(id, null, ct);
    }

    public async Task<int> CreateFromSalesLineAsync(CreateWorkOrderFromSalesLineRequest request, CancellationToken ct)
    {
        if (request.Quantity <= 0)
            throw new ArgumentException("Miktar 0'dan buyuk olmali.", nameof(request.Quantity));

        var line = await GetSalesLineAsync(request.SourceDocumentId, request.SourceLineId, ct)
            ?? throw new InvalidOperationException("Kaynak sipariş satırı bulunamadı.");

        var allocated = await _workOrders.GetAllocatedQuantityForLineAsync(request.SourceLineId, ct);
        var remaining = line.Quantity - allocated;
        if (request.Quantity > remaining + 0.0001m)
            throw new InvalidOperationException($"Kalan açık miktar ({remaining}) yetersiz.");

        if (request.TargetWorkOrderId.HasValue)
        {
            // Toplama: mevcut emire allocation ekle + PlannedQuantity'i artir
            var target = await _workOrders.GetAsync(request.TargetWorkOrderId.Value, ct)
                ?? throw new InvalidOperationException("Hedef is emri bulunamadi.");
            if (target.Status is not (WorkOrderStatus.Planned or WorkOrderStatus.Released))
                throw new InvalidOperationException("Sadece Planned veya Released emire eklenebilir.");
            if (target.ItemId != line.ItemId || target.ConfigId != line.CombinationId)
                throw new InvalidOperationException("Mamul/konfigurasyon uyusmuyor.");

            await _workOrders.AddSourceAsync(target.Id, request.SourceDocumentId, request.SourceLineId, request.Quantity, ct);
            await _workOrders.UpdateAsync(target.Id, new UpdateWorkOrderRequest(
                PlannedQuantity: target.PlannedQuantity + request.Quantity,
                UnitId: target.UnitId,
                PlannedStartDate: target.PlannedStartDate,
                PlannedEndDate: target.PlannedEndDate,
                Priority: target.Priority,
                AssignedUserId: target.AssignedUserId,
                WarehouseLocationId: target.WarehouseLocationId,
                RoutingId: target.RoutingId,
                DefaultMachineId: target.DefaultMachineId,
                AssignedPersonnelId: target.AssignedPersonnelId,
                Notes: target.Notes), null, ct);
            return target.Id;
        }

        // Yeni emir
        var newId = await CreateAsync(new CreateWorkOrderRequest(
            ItemId: line.ItemId,
            ConfigId: line.CombinationId,
            PlannedQuantity: request.Quantity,
            UnitId: line.UnitId,
            PlannedStartDate: null,
            PlannedEndDate: null,
            Priority: WorkOrderPriority.Medium,
            AssignedUserId: null,
            WarehouseLocationId: line.LocationId,
            RoutingId: null, // CreateAsync icinde Item bazli auto-resolve
            DefaultMachineId: null,
            AssignedPersonnelId: null,
            Notes: null), ct);

        await _workOrders.AddSourceAsync(newId, request.SourceDocumentId, request.SourceLineId, request.Quantity, ct);
        return newId;
    }

    public Task<IReadOnlyCollection<WorkOrderListItemDto>> ListEligibleForMergeAsync(int itemId, int? configId, CancellationToken ct)
        => _workOrders.ListEligibleForMergeAsync(itemId, configId, ct);

    public Task<decimal> GetAllocatedQuantityForLineAsync(int sourceLineId, CancellationToken ct)
        => _workOrders.GetAllocatedQuantityForLineAsync(sourceLineId, ct);

    private async Task<DocumentLine?> GetSalesLineAsync(int documentId, int lineId, CancellationToken ct)
    {
        var lines = await _documents.GetLinesAsync(documentId, ct);
        return lines.FirstOrDefault(l => l.Id == lineId);
    }

    // ── Faz 2 — BOM Patlatma ────────────────────────────────────────────────────
    public async Task<ExplodeBomResultDto> ExplodeBomAsync(int workOrderId, CancellationToken ct)
    {
        var wo = await _workOrders.GetAsync(workOrderId, ct)
            ?? throw new InvalidOperationException("Iş emri bulunamadi.");

        if (wo.PlannedQuantity <= 0)
            throw new InvalidOperationException("Planlanan miktar 0'dan büyük olmalı — patlatma yapılamaz.");

        var bom = await _logisticsConfig.GetBOMByItemAsync(wo.ItemId, wo.ConfigId, ct)
            ?? throw new InvalidOperationException(
                $"Bu mamul için tanımlı bir reçete (BOM) bulunamadı: {wo.ItemCode ?? "#" + wo.ItemId}"
                + (wo.ConfigId.HasValue ? $" / Konfig {wo.ConfigId}" : "")
                + ". Önce Lojistik → Ürün Ağacı'nda reçete tanımlayın.");

        var components = bom.Lines.Select(l => new WorkOrderComponent
        {
            WorkOrderId      = workOrderId,
            ItemId           = l.ItemId,
            ConfigId         = l.ConfigId,
            // RequiredQty = bomLine.Quantity × wo.PlannedQuantity × (1 + ScrapRatio)
            RequiredQuantity = l.Quantity * wo.PlannedQuantity * (1m + l.ScrapRatio),
            IssuedQuantity   = 0m,
            ScrapRate        = l.ScrapRatio,
            UnitId           = null, // BOMLineWithName birim taşımıyor; ileride Item default birimi sızdırılabilir
            Notes            = null,
        }).ToList();

        await _workOrderComponents.ReplaceForWorkOrderAsync(workOrderId, components, ct);

        return new ExplodeBomResultDto(
            WorkOrderId:    workOrderId,
            BomId:          bom.Id,
            ComponentCount: components.Count,
            Multiplier:     wo.PlannedQuantity);
    }

    public Task<IReadOnlyCollection<WorkOrderComponentDto>> GetComponentsAsync(int workOrderId, CancellationToken ct)
        => _workOrderComponents.GetByWorkOrderAsync(workOrderId, ct);

    private static void ValidateTransition(WorkOrderStatus current, WorkOrderStatus next)
    {
        // Cancelled her durumdan (Closed haric) kabul, Closed -> hicbir sey
        if (current == WorkOrderStatus.Closed)
            throw new InvalidOperationException("Kapali is emrinin durumu degistirilemez.");
        if (current == WorkOrderStatus.Cancelled && next != WorkOrderStatus.Cancelled)
            throw new InvalidOperationException("Iptal edilmis is emri tekrar baslatilamaz; revize edin.");

        if (next == WorkOrderStatus.Cancelled) return; // her gecisten iptal kabul

        bool ok = (current, next) switch
        {
            (WorkOrderStatus.Planned, WorkOrderStatus.Released) => true,
            (WorkOrderStatus.Released, WorkOrderStatus.InProgress) => true,
            (WorkOrderStatus.Released, WorkOrderStatus.Planned) => true, // henüz hareket islenmediyse geri al
            (WorkOrderStatus.InProgress, WorkOrderStatus.Completed) => true,
            (WorkOrderStatus.Completed, WorkOrderStatus.Closed) => true,
            (WorkOrderStatus.Completed, WorkOrderStatus.InProgress) => true, // hata duzeltme
            _ => current == next,
        };
        if (!ok)
            throw new InvalidOperationException($"Gecersiz durum gecisi: {current} → {next}");
    }
}
