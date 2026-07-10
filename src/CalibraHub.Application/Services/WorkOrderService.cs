using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
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
    // 2026-05-20: Belge numarası türetme önceliği:
    //   1. DocumentNumberRule (Tasarım Kuralları → Belge Numarası ekranı) — aktif kural
    //      varsa onun PREFIX + YIL/AY + SAYAÇ formatı uygulanır (DocumentService pattern'i).
    //   2. NumeratorService (CompanyParameter "NumeratorMask"/"NumeratorResetPolicy")
    //   3. DefaultNumberMask fallback: WO-{yy}-{seq:9}
    // Yeni kullanıcılar Belge Tipi="Is Emri" için kural tanımlayınca anında etkin olur.
    private const string DefaultNumberMask = "WO-{yy}-{seq:9}";
    private const string WorkOrderTypeCode = "is_emri";

    private readonly IWorkOrderRepository _workOrders;
    private readonly INumeratorService _numerator;
    private readonly ICompanyParameterService _parameters;
    private readonly IDocumentRepository _documents;
    private readonly IWorkOrderOperationRepository _workOrderOperations;
    private readonly IWorkOrderComponentRepository _workOrderComponents;
    private readonly ILogisticsConfigurationRepository _logisticsConfig;
    private readonly IDocumentNumberService? _docNumberService;
    private readonly IDocumentTypeRepository? _documentTypeRepo;
    private readonly IArgeProjectRepository? _argeProjects;
    private readonly IAuditTrailService? _audit;

    public WorkOrderService(
        IWorkOrderRepository workOrders,
        INumeratorService numerator,
        ICompanyParameterService parameters,
        IDocumentRepository documents,
        IWorkOrderOperationRepository workOrderOperations,
        IWorkOrderComponentRepository workOrderComponents,
        ILogisticsConfigurationRepository logisticsConfig,
        IDocumentNumberService? docNumberService = null,
        IDocumentTypeRepository? documentTypeRepo = null,
        IArgeProjectRepository? argeProjects = null,
        IAuditTrailService? audit = null)
    {
        _workOrders = workOrders;
        _numerator = numerator;
        _parameters = parameters;
        _documents = documents;
        _workOrderOperations = workOrderOperations;
        _workOrderComponents = workOrderComponents;
        _logisticsConfig = logisticsConfig;
        _docNumberService = docNumberService;
        _documentTypeRepo = documentTypeRepo;
        _argeProjects = argeProjects;
        _audit = audit;
    }

    /// <summary>
    /// İş emri numarası türetme — DocumentNumberRule (admin tanımlı kural) önce kontrol
    /// edilir; bulunmazsa NumeratorService fallback. DocumentService.ResolveNextDocumentNumberAsync
    /// pattern'inin birebir aynısı (geriye uyum: kural yoksa eski WO-{yy}-{seq:9} çalışır).
    /// </summary>
    private async Task<string> ResolveOrderNumberAsync(DateTime orderDate, CancellationToken ct)
    {
        // 1) DocumentNumberRule — is_emri belge tipinin aktif kuralı varsa onu uygula
        if (_docNumberService is not null && _documentTypeRepo is not null)
        {
            var type = await _documentTypeRepo.GetByCodeAsync(WorkOrderTypeCode, ct);
            if (type is not null && type.Id > 0)
            {
                var ctx = new DocumentNumberContext(
                    DocumentTypeId: type.Id,
                    ContactId:      null,
                    ContactGroupId: null,
                    UserId:         null,   // ileride: current user — controller'dan pass
                    BranchId:       null,
                    IssueDate:      orderDate);
                var generated = await _docNumberService.GenerateNextAsync(ctx, ct);
                if (!string.IsNullOrWhiteSpace(generated)) return generated;
            }
        }

        // 2) Fallback: NumeratorService (CompanyParameter mask veya hardcoded default)
        return await _numerator.GetNextNumberAsync("WORK_ORDER", FormCode, DefaultNumberMask, ct);
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
        if (_documentTypeRepo is null)
            throw new InvalidOperationException("Belge tipi deposu (IDocumentTypeRepository) kayitli degil.");

        var orderDate = DateTime.UtcNow;
        var orderNumber = await ResolveOrderNumberAsync(orderDate, ct);

        // 2026-07-02: Document companion modeli — belge kimligi (numara/tarih/notlar) once
        // Document'ta olusturulur (ArgeProjectService.SaveAsync ile ayni desen), WorkOrder
        // sonra bu DocumentId'ye baglanir.
        var type = await _documentTypeRepo.GetByCodeAsync(WorkOrderTypeCode, ct)
            ?? throw new InvalidOperationException("'is_emri' belge tipi tanimli degil (DB init calismadi mi?).");
        var documentId = await _documents.UpsertAsync(new Document
        {
            DocumentNumber = orderNumber,
            DocumentTypeId = type.Id,
            DocumentDate = orderDate,
            Status = DocumentStatus.Draft,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        }, ct);

        var defaultLocationId = request.WarehouseLocationId
            ?? await _parameters.GetIntAsync(FormCode, "DefaultLocationId", ct);
        int? defaultUserId = request.AssignedUserId;
        if (!defaultUserId.HasValue)
        {
            var rawUserId = await _parameters.GetStringAsync(FormCode, "DefaultAssignedUserId", ct);
            if (int.TryParse(rawUserId, out var parsedUser)) defaultUserId = parsedUser;
        }
        var defaultPlanDays = await _parameters.GetIntAsync(FormCode, "DefaultPlanDays", ct) ?? 7;
        // Per-WO switch ("Üretime Planla") öncelikli — request.AutoRelease set ise onu kullan,
        // yoksa CompanyParameter fallback. Frontend default ON gönderir; user kapatırsa false.
        var autoRelease = request.AutoRelease
            ?? (await _parameters.GetBoolAsync(FormCode, "AutoRelease", ct) ?? false);

        var plannedEnd = request.PlannedEndDate ?? orderDate.AddDays(defaultPlanDays);

        // Routing resolve: request'te verildiyse onu kullan, yoksa Item bazlı arama yap (Faz 3a default rota).
        var resolvedRoutingId = request.RoutingId
            ?? await _workOrders.FindRoutingForItemAsync(request.ItemId, request.ConfigId, ct);

        // AR-GE Faz 3: proje baglantisi. Acikca verildiyse onu kullan; yoksa ItemId AR-GE seri/prototip
        // mamuluyse otomatik turet (mamul -> ArgeProductionLink/ArgePrototype -> proje).
        var argeProjectId = request.ArgeProjectId
            ?? (_argeProjects is null ? null : await _argeProjects.FindProjectIdByItemAsync(request.ItemId, ct));

        var entity = new WorkOrder
        {
            DocumentId = documentId,
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
            ArgeProjectId = argeProjectId,
        };

        var newId = await _workOrders.CreateAsync(entity, ct);

        // AutoRelease ise routing varsa direkt operasyonları patlat.
        if (autoRelease && resolvedRoutingId.HasValue)
        {
            await _workOrderOperations.ExplodeFromRoutingAsync(newId, resolvedRoutingId.Value, ct);
        }

        // İşlem logu — yeni iş emri; ilk değer dökümü için kaydedilen DTO (display alanlarıyla) okunur
        if (_audit is not null)
        {
            WorkOrderDto? createdForAudit = null;
            try { createdForAudit = await _workOrders.GetAsync(newId, ct); } catch { }
            _audit.LogInsert("WorkOrder", newId, orderNumber,
                detail: $"Planlanan {AuditDiff.Normalize(request.PlannedQuantity)}" +
                        (autoRelease ? " · Yayımlandı" : ""),
                snapshot: createdForAudit);
        }

        return newId;
    }

    public async Task UpdateAsync(int id, UpdateWorkOrderRequest request, CancellationToken ct)
    {
        if (request.PlannedQuantity <= 0)
            throw new ArgumentException("Planlanan miktar 0'dan buyuk olmali.", nameof(request.PlannedQuantity));

        // İşlem logu: eski durumu mutasyondan ÖNCE oku (yalnızca audit için)
        WorkOrderDto? auditOld = null;
        if (_audit is not null)
        {
            try { auditOld = await _workOrders.GetAsync(id, ct); } catch { }
        }

        await _workOrders.UpdateAsync(id, request, null, ct);

        // Notlar artik Document.notes'ta — WorkOrder tarafi guncellendikten sonra ayrica yazilir.
        var current = await _workOrders.GetAsync(id, ct);
        if (current is not null)
        {
            var doc = await _documents.GetByIdAsync(current.DocumentId, ct);
            if (doc is not null)
            {
                doc.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
                await _documents.UpsertAsync(doc, ct);
            }
        }

        // İşlem logu — request'teki alanlar eski DTO ile ad eşleşmesiyle diff'lenir,
        // yalnızca değişen alanlar loglanır (hiç değişiklik yoksa log yazılmaz).
        if (_audit is not null && auditOld is not null)
        {
            _audit.LogUpdate("WorkOrder", id, auditOld.OrderNumber, auditOld, request);
        }
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

        // İşlem logu — iptal kullanıcı gözünden silmedir (LogDelete), diğer geçişler durum değişikliği
        if (_audit is not null && current.Status != newStatus)
        {
            if (newStatus == WorkOrderStatus.Cancelled)
                _audit.LogDelete("WorkOrder", id, current.OrderNumber, detail: "İptal edildi");
            else
                _audit.LogChanges("WorkOrder", id, current.OrderNumber,
                    [new AuditFieldChange("Status", "Durum", current.Status.ToString(), newStatus.ToString())]);
        }
    }

    public async Task<int> ReviseAsync(int id, CancellationToken ct)
    {
        var current = await _workOrders.GetAsync(id, ct)
            ?? throw new InvalidOperationException("Is emri bulunamadi.");

        if (current.Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled)
            throw new InvalidOperationException("Kapali veya iptal edilmis is emri revize edilemez.");

        if (_documentTypeRepo is null)
            throw new InvalidOperationException("Belge tipi deposu (IDocumentTypeRepository) kayitli degil.");

        // 2026-07-02: DocumentService'in teklif revizyon deseniyle ayni — yeni bir Document
        // satiri acilir (ParentDocumentId=eski, RevisionNo+1), suffix hack YOK (numara motoru
        // DocumentNumberRule ile native uretir).
        var oldDoc = await _documents.GetByIdAsync(current.DocumentId, ct)
            ?? throw new InvalidOperationException("Belge bulunamadi.");

        var revisionDate = DateTime.UtcNow;
        var newNumber = await ResolveOrderNumberAsync(revisionDate, ct);
        var type = await _documentTypeRepo.GetByCodeAsync(WorkOrderTypeCode, ct)
            ?? throw new InvalidOperationException("'is_emri' belge tipi tanimli degil (DB init calismadi mi?).");

        var newDocumentId = await _documents.UpsertAsync(new Document
        {
            DocumentNumber = newNumber,
            DocumentTypeId = type.Id,
            DocumentDate = revisionDate,
            Status = DocumentStatus.Draft,
            RevisionNo = oldDoc.RevisionNo + 1,
            ParentDocumentId = oldDoc.Id,
            Notes = oldDoc.Notes,
        }, ct);

        var revisionId = await _workOrders.CreateRevisionAsync(id, newDocumentId, null, ct);

        // İşlem logu — revizyon yeni kayıttır; eski emir repo tarafında Cancelled olur
        if (_audit is not null)
        {
            WorkOrderDto? createdForAudit = null;
            try { createdForAudit = await _workOrders.GetAsync(revisionId, ct); } catch { }
            _audit.LogInsert("WorkOrder", revisionId, newNumber,
                detail: $"Revizyon — kaynak {current.OrderNumber}",
                snapshot: createdForAudit);
        }

        return revisionId;
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
                Notes: target.Notes,
                ArgeProjectId: target.ArgeProjectId), null, ct);

            // İşlem logu — toplama: mevcut emrin planlanan miktarı arttı
            _audit?.LogChanges("WorkOrder", target.Id, target.OrderNumber,
                [new AuditFieldChange("PlannedQuantity", "Planlanan Miktar",
                    AuditDiff.Normalize(target.PlannedQuantity),
                    AuditDiff.Normalize(target.PlannedQuantity + request.Quantity))],
                detail: "Sipariş satırından toplama");
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

    public Task IssueComponentAsync(IssueWorkOrderComponentRequest request, CancellationToken ct)
    {
        if (request.WorkOrderComponentId <= 0) throw new ArgumentException("Bileşen kaydı zorunlu.");
        if (request.Quantity <= 0) throw new ArgumentException("Miktar 0'dan büyük olmalı.");
        if (request.OperatorPersonnelId <= 0) throw new ArgumentException("Operatör (Personnel) zorunlu.");
        return _workOrderComponents.IssueAsync(request.WorkOrderComponentId, request.Quantity, request.OperatorPersonnelId, ct);
    }

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
