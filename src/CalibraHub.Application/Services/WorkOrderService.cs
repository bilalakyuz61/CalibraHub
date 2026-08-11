using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Logging;

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
    private readonly IDocumentSourceRepository? _docSourceRepo;
    private readonly IDocumentLineLinkRepository? _lineLinks;
    private readonly ILogger<WorkOrderService>? _logger;

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
        IAuditTrailService? audit = null,
        IDocumentSourceRepository? docSourceRepo = null,
        IDocumentLineLinkRepository? lineLinks = null,
        ILogger<WorkOrderService>? logger = null)
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
        _docSourceRepo = docSourceRepo;
        _lineLinks = lineLinks;
        _logger = logger;
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

        // Malzeme Belge Kilitleri ("is_emri") — WorkOrderEdit "Mamul Kodu" alanı standart
        // rehber (ITEMS_FINISHED guide) kullanır; rehber lookup'ta bu kilit filtrelenemez
        // (custom Guide bypass — SalesController/WarehouseController'daki lookup-time NOT IN
        // deseni burada uygulanamıyor). Bu yüzden gerçek kapı save anındadır.
        var lockedForWorkOrder = await _logisticsConfig.GetLockedItemIdsByDocTypeAsync(
            ItemDocumentLockTypes.WorkOrderCode, ct);
        if (lockedForWorkOrder.Contains(request.ItemId))
        {
            var lockedItem = (await _logisticsConfig.GetItemsByIdsAsync(new[] { request.ItemId }, ct))
                .FirstOrDefault();
            var label = lockedItem?.Code ?? $"#{request.ItemId}";
            throw new ArgumentException(
                $"'{label}' malzemesi İş Emri / Üretim için kilitli — Malzeme Belge Kilitleri ekranından kilidi kaldırın.",
                nameof(request.ItemId));
        }

        // İş Emri Tarihi (2026-08-01) — frontend'den doluysa (WorkOrderEdit editable alan) onu
        // kullan; boş/null gelirse mevcut davranış (bugün) korunur.
        var orderDate = request.OrderDate ?? DateTime.UtcNow;
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
        // İş Emri Tarihi (2026-08-01) — WorkOrder.OrderDate, Document.DocumentDate'in view alias'ı;
        // request.OrderDate doluysa güncellenir, boş/null gelirse mevcut tarih korunur.
        var current = await _workOrders.GetAsync(id, ct);
        if (current is not null)
        {
            var doc = await _documents.GetByIdAsync(current.DocumentId, ct);
            if (doc is not null)
            {
                doc.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
                if (request.OrderDate.HasValue) doc.DocumentDate = request.OrderDate.Value;
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

        // DocumentLineLink dual-write reverse (Faz 1b-i, best-effort) — iş emri Cancelled'a
        // geçtiğinde tahsis link'lerini pasifleştir (tasarım KN-3). Yalnız gerçek bir duruma
        // geçişte çalışır (aynı statüye tekrar "geçiş" — örn. zaten Cancelled — atlanır).
        if (current.Status != newStatus && newStatus == WorkOrderStatus.Cancelled)
        {
            await TryReverseWorkOrderLinksAsync(current.DocumentId, ct);
        }

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

        // DocumentLineLink dual-write (Faz 1b-i, best-effort) — bkz. TryRelinkWorkOrderRevisionAsync KDoc'u.
        await TryRelinkWorkOrderRevisionAsync(current.DocumentId, revisionId, ct);

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

        // Malzeme Belge Kilitleri ("is_emri") — satış satırından otomatik iş emri açma da
        // aynı kapıya tabi (CreateAsync'teki manuel WorkOrderEdit yolu ile aynı kural).
        var lockedForWorkOrder = await _logisticsConfig.GetLockedItemIdsByDocTypeAsync(
            ItemDocumentLockTypes.WorkOrderCode, ct);
        if (lockedForWorkOrder.Contains(line.ItemId))
        {
            var lockedItem = (await _logisticsConfig.GetItemsByIdsAsync(new[] { line.ItemId }, ct))
                .FirstOrDefault();
            var label = lockedItem?.Code ?? $"#{line.ItemId}";
            throw new InvalidOperationException(
                $"'{label}' malzemesi İş Emri / Üretim için kilitli — Malzeme Belge Kilitleri ekranından kilidi kaldırın.");
        }

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
            // Lineage kenarı (İlişkili Belgeler) — WorkOrderSource miktar tahsisinden AYRI,
            // salt belge-graf gezinme amaçlı DocumentSource kenarı (bkz. LinkWorkOrderLineageAsync KDoc'u).
            await LinkWorkOrderLineageAsync(target.DocumentId, request.SourceDocumentId, ct);
            // DocumentLineLink dual-write (Faz 1b-i, best-effort) — bkz. TryLinkWorkOrderAllocAsync KDoc'u.
            await TryLinkWorkOrderAllocAsync(request.SourceLineId, request.SourceDocumentId, target.DocumentId, target.Id, request.Quantity, ct);
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

        // Lineage kenarı (bkz. LinkWorkOrderLineageAsync KDoc'u) — yeni emrin DocumentId'si
        // CreateAsync içinde üretildi, burada tek bir GetAsync ile okunur (CreateAsync'in kendi
        // dönüş değeri WorkOrder.Id'dir, Document.Id değil).
        var createdWo = await _workOrders.GetAsync(newId, ct);
        if (createdWo is not null)
        {
            await LinkWorkOrderLineageAsync(createdWo.DocumentId, request.SourceDocumentId, ct);
            // DocumentLineLink dual-write (Faz 1b-i, best-effort) — bkz. TryLinkWorkOrderAllocAsync KDoc'u.
            await TryLinkWorkOrderAllocAsync(request.SourceLineId, request.SourceDocumentId, createdWo.DocumentId, newId, request.Quantity, ct);
        }

        return newId;
    }

    /// <summary>
    /// İş emri belgesini (türetilen) kaynak sipariş belgesine (kaynak) DocumentSource köprüsüyle
    /// bağlar — <c>/Document/Lineage</c> paneli (İlişkili Belgeler) bu köprüyü okuyup iş
    /// emri↔sipariş bağını gösterir. WorkOrderSource'taki miktar tahsisinden BAĞIMSIZ, salt
    /// gezinme/görsel amaçlı bir kenardır (2026-07-20). AddAsync idempotent (IF NOT EXISTS) —
    /// toplama akışında aynı çift birden fazla kez eklenmeye çalışılsa da tekrarlanmaz.
    ///
    /// İş emri iptal edildiğinde (Status=Cancelled) bu kenar BİLEREK temizlenmiyor — lineage
    /// düğümü WorkOrder'ın kendi Document.IsActive bayrağına göre "Silinmiş" gösterilir. NOT:
    /// bu bayrak iptalde OTOMATİK 0 OLMUYOR (yalnız WorkOrder.Status değişiyor) — bkz. rapor.
    ///
    /// _docSourceRepo kayıtlı değilse (nullable — cross-module bağımlılık, opsiyonel) sessizce
    /// no-op. Gerçek bir hata (ör. şema/kolon sorunu) ise YUTULUR ama LOGLANIR — iş emri
    /// bağlama akışı hiçbir zaman bu yüzden bozulmaz (bkz. CLAUDE.md "sessiz kırık" kural #2:
    /// catch boş bırakılmaz, exception loglanır).
    /// </summary>
    private async Task LinkWorkOrderLineageAsync(int workOrderDocumentId, int sourceDocumentId, CancellationToken ct)
    {
        if (_docSourceRepo is null) return;
        try
        {
            await _docSourceRepo.EnsureSchemaAsync(ct);
            await _docSourceRepo.AddAsync(workOrderDocumentId, sourceDocumentId, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "[WorkOrder] Lineage kenarı eklenemedi: WO Document {WoDocumentId} -> Source {SourceDocumentId}",
                workOrderDocumentId, sourceDocumentId);
        }
    }

    // ── DocumentLineLink dual-write — Faz 1b-i (2026-07-20, "B: iş emri" mekanizması) ─────────
    // bkz. DocumentLineLink-Tasarim.md §5 (eşleme B → Link) ve §8 (Faz 1b defensive karar).
    // KRİTİK ilke: bu üç yardımcı, WorkOrderSource'a yazan/onu tersine çeviren ana işlemlerin
    // TAMAMLANMASINDAN SONRA çağrılır, kendi try/catch'i içinde çalışır ve ASLA exception
    // fırlatmaz — link tablosu bir bug taşırsa canlı iş emri kaydı/iptali/revizyonu kırılmaz
    // (CLAUDE.md audit trail deseniyle birebir aynı "sessiz kırık" önleme kuralı: boş catch
    // yasak, exception loglanır). _lineLinks DI'da her zaman kayıtlı olsa da (Program.cs)
    // nullable bırakıldı — _docSourceRepo ile aynı "opsiyonel best-effort bağımlılık" üslubu.
    // Ayrı bir transaction'da çalışır; ana WorkOrderSource insert/update ile ORTAK transaction
    // PAYLAŞMAZ (tasarım §8: "Link, ana kayıttan ayrı; aynı transaction'a SOKMA").

    /// <summary>
    /// Bir iş emri tahsisini (Satış Sipariş kalemi → İş Emri, <c>WorkOrderSource</c> ile aynı
    /// olay) <c>DocumentLineLink</c>'e <c>LinkType.WorkOrderAlloc</c> (20) olarak paralel yazar.
    /// <c>CreateFromSalesLineAsync</c>'in hem "toplama" (mevcut emre ekleme) hem "yeni emir"
    /// dalından, ana <c>AddSourceAsync</c> çağrısı başarıyla TAMAMLANDIKTAN SONRA çağrılır.
    /// CreatedById şu an bu serviste (AddSourceAsync/ChangeStatusAsync gibi diğer repo
    /// çağrılarıyla aynı şekilde) hep NULL geçiliyor — dosyada henüz current-user thread'i yok,
    /// bu metot da o mevcut deseni bozmuyor.
    /// </summary>
    private async Task TryLinkWorkOrderAllocAsync(int sourceLineId, int sourceDocId, int targetDocId, int targetWorkOrderId, decimal quantity, CancellationToken ct)
    {
        if (_lineLinks is null) return;
        try
        {
            // Merge-aware: ayni (satir, WO) tekrar tahsisi tek link satirinda toplanir
            // (WorkOrderAlloc'ta TargetLineId=NULL -> UX ihlali olmasin; WorkOrderSource
            // satir+WO SUM'u ile birebir kalsin). Bkz. IDocumentLineLinkRepository KDoc'u.
            await _lineLinks.InsertOrAccumulateAsync(new DocumentLineLinkEntry(
                LinkType.WorkOrderAlloc,
                SourceLineId: sourceLineId,
                SourceDocId: sourceDocId,
                TargetLineId: null,
                TargetDocId: targetDocId,
                TargetWorkOrderId: targetWorkOrderId,
                Quantity: quantity), userId: null, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "[WorkOrder] DocumentLineLink insert edilemedi: SourceLine {SourceLineId} (Doc {SourceDocId}) -> WO {WorkOrderId} (Doc {TargetDocId})",
                sourceLineId, sourceDocId, targetWorkOrderId, targetDocId);
        }
    }

    /// <summary>
    /// Bir iş emrinin (<paramref name="workOrderDocumentId"/> = WorkOrder.DocumentId) AKTİF
    /// tahsis link'lerini (yalnız <c>LinkType.WorkOrderAlloc</c> — tür filtreli, bkz.
    /// <see cref="IDocumentLineLinkRepository.ReverseByTargetAsync"/> KDoc'u) pasifleştirir.
    /// İş emri iptalinde (<see cref="ChangeStatusAsync"/>) VE revizyonda (eski emrin link'leri,
    /// <see cref="TryRelinkWorkOrderRevisionAsync"/> içinden) çağrılır.
    /// </summary>
    private async Task TryReverseWorkOrderLinksAsync(int workOrderDocumentId, CancellationToken ct)
    {
        if (_lineLinks is null) return;
        try
        {
            await _lineLinks.ReverseByTargetAsync(workOrderDocumentId, userId: null, LinkType.WorkOrderAlloc, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "[WorkOrder] DocumentLineLink reverse edilemedi: WO Document {WoDocumentId}",
                workOrderDocumentId);
        }
    }

    /// <summary>
    /// Revizyon senkronu — <c>CreateRevisionAsync</c> (repo, tek transaction) eski WO'yu
    /// Cancelled yapıp <c>WorkOrderSource</c> satırlarını yeni WO'ya kopyaladıktan SONRA
    /// çağrılır; link tablosunda aynı iki adımı best-effort yansıtır: (1) eski WO'nun tahsis
    /// link'lerini pasifleştir (<see cref="TryReverseWorkOrderLinksAsync"/> — kendi try/catch'i
    /// var, asla fırlatmaz), (2) yeni WO'ya kopyalanan her source için link 20 ekle
    /// (<see cref="TryLinkWorkOrderAllocAsync"/> — o da kendi try/catch'i var). Bu metodun
    /// kendi try/catch'i yalnız "yeni WO'yu ve kopyalanan source listesini oku" adımını
    /// sarar — o okuma başarısız olursa (silinmiş kayıt, geçici bağlantı hatası vb.) link
    /// senkronu atlanır ama revizyon işleminin kendisi (zaten tamamlanmış) etkilenmez.
    /// </summary>
    private async Task TryRelinkWorkOrderRevisionAsync(int oldWorkOrderDocumentId, int newWorkOrderId, CancellationToken ct)
    {
        if (_lineLinks is null) return;

        await TryReverseWorkOrderLinksAsync(oldWorkOrderDocumentId, ct);

        try
        {
            var newWo = await _workOrders.GetAsync(newWorkOrderId, ct);
            if (newWo is null) return;

            var copiedSources = await _workOrders.GetSourcesAsync(newWorkOrderId, ct);
            foreach (var src in copiedSources)
            {
                await TryLinkWorkOrderAllocAsync(src.SourceLineId, src.SourceDocumentId, newWo.DocumentId, newWorkOrderId, src.AllocatedQuantity, ct);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "[WorkOrder] Revizyon icin yeni WO source'lari okunamadi (DocumentLineLink senkronu atlandi): eski WO Document {OldDocId} -> yeni WO {NewWorkOrderId}",
                oldWorkOrderDocumentId, newWorkOrderId);
        }
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

        // Üretimi başlamış emirde reçete düzeltme kilidi (2026-08-06 — ALLOW_STARTED_WO_RECIPE_EDIT).
        await EnsureRecipeEditableAsync(wo.Status, ct);

        // 2026-08-06 versiyonlama: iş emrinin reçete seçimi. NULL = BAZ reçeteyi CANLI takip et
        // (kullanıcı kararı: baz değişirse baz-seçili emirler güncel bazdan üretilmeye devam eder).
        // Dolu = seçili versiyonun güncel hali. Versiyon silinmişse/başka mamule aitse net hata.
        var selectedBomId = await _workOrders.GetBomIdAsync(workOrderId, ct);
        BOMWithNames? bom;
        if (selectedBomId is > 0)
        {
            bom = await _logisticsConfig.GetBOMByIdWithNamesAsync(selectedBomId.Value, ct);
            if (bom is null || bom.ItemId != wo.ItemId)
                throw new InvalidOperationException(
                    "İş emrinde seçili reçete versiyonu artık mevcut değil (silinmiş/pasif olabilir). "
                    + "Reçete sekmesinden baz reçeteyi veya başka bir versiyonu seçip tekrar deneyin.");
        }
        else
        {
            bom = await _logisticsConfig.GetBOMByItemAsync(wo.ItemId, wo.ConfigId, ct)
                ?? throw new InvalidOperationException(
                    $"Bu mamul için tanımlı bir reçete (BOM) bulunamadı: {wo.ItemCode ?? "#" + wo.ItemId}"
                    + (wo.ConfigId.HasValue ? $" / Konfig {wo.ConfigId}" : "")
                    + ". Önce Lojistik → Ürün Ağacı'nda reçete tanımlayın.");
        }

        // Re-explode koruması (2026-07-31, CLAUDE.md kural-3 — sessiz veri kaybı): ReplaceForWorkOrderAsync
        // DELETE+INSERT olduğu için "Patlat"a tekrar basınca kullanıcının elle seçtiği FromLocationId
        // sessizce silinirdi. Eski bileşenleri (ItemId,ConfigId) anahtarıyla önceden okuyup override'ı koru.
        var existing = await _workOrderComponents.GetByWorkOrderAsync(workOrderId, ct);

        // Sarf başlamış emirde yeniden patlatma ENGELLENİR (2026-08-06 kullanıcı kararı):
        // DELETE+INSERT IssuedQuantity'yi sıfırlar → çıkış geçmişi ile mutabakat kopar.
        // Bu guard parametreden BAĞIMSIZDIR (ALLOW_STARTED_WO_RECIPE_EDIT açık olsa bile).
        if (existing.Any(e => e.IssuedQuantity > 0))
            throw new InvalidOperationException(
                "Bu iş emrinde bileşen sarfı başlamış — yeniden patlatma yapılamaz "
                + "(çıkış geçmişi kaybolur). Gerekli düzeltmeleri bileşen listesi üzerinde yapın.");
        var previousLocationByKey = existing
            .Where(e => e.FromLocationId.HasValue)
            .GroupBy(e => (e.ItemId, e.ConfigId))
            .ToDictionary(g => g.Key, g => g.First().FromLocationId);

        // Malzemenin varsayılan lokasyonu (ItemLocation.IsDefault) — kullanıcı override'ı yoksa
        // bileşen bu lokasyonla önerilir; hiç varsayılan yoksa NULL kalır (sarf motoru WO deposuna düşer).
        var defaultLocationByItem = new Dictionary<int, int?>();
        async Task<int?> ResolveDefaultLocationAsync(int itemId)
        {
            if (defaultLocationByItem.TryGetValue(itemId, out var cached)) return cached;
            int? resolved = null;
            try
            {
                var locations = await _logisticsConfig.GetItemLocationsAsync(itemId, ct);
                resolved = locations.FirstOrDefault(l => l.IsDefault)?.LocationId;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[WorkOrder.ExplodeBom] ItemId={ItemId} varsayılan lokasyon okunamadı.", itemId);
            }
            defaultLocationByItem[itemId] = resolved;
            return resolved;
        }

        var components = new List<WorkOrderComponent>();
        foreach (var l in bom.Lines)
        {
            var key = (l.ItemId, l.ConfigId);
            int? fromLocationId = previousLocationByKey.TryGetValue(key, out var preserved)
                ? preserved
                : await ResolveDefaultLocationAsync(l.ItemId);

            components.Add(new WorkOrderComponent
            {
                WorkOrderId      = workOrderId,
                ItemId           = l.ItemId,
                ConfigId         = l.ConfigId,
                // RequiredQty = bomLine.Quantity × wo.PlannedQuantity × (1 + ScrapRatio)
                RequiredQuantity = l.Quantity * wo.PlannedQuantity * (1m + l.ScrapRatio),
                IssuedQuantity   = 0m,
                ScrapRate        = l.ScrapRatio,
                UnitId           = null, // BOMLineWithName birim taşımıyor; ileride Item default birimi sızdırılabilir
                FromLocationId   = fromLocationId,
                Notes            = null,
            });
        }

        await _workOrderComponents.ReplaceForWorkOrderAsync(workOrderId, components, ct);

        return new ExplodeBomResultDto(
            WorkOrderId:    workOrderId,
            BomId:          bom.Id,
            ComponentCount: components.Count,
            Multiplier:     wo.PlannedQuantity);
    }

    public Task<IReadOnlyCollection<WorkOrderComponentDto>> GetComponentsAsync(int workOrderId, CancellationToken ct)
        => _workOrderComponents.GetByWorkOrderAsync(workOrderId, ct);

    // ── Reçete versiyonlama + iş emri bazında bileşen özelleştirme (2026-08-06) ──

    /// <summary>
    /// Üretimi başlamış (InProgress ve sonrası) emirde reçete düzeltme kilidi —
    /// ALLOW_STARTED_WO_RECIPE_EDIT üretim parametresi açık değilse reddedilir.
    /// Planned/Released serbesttir; Completed/Closed/Cancelled HER ZAMAN kilitli.
    /// </summary>
    private async Task EnsureRecipeEditableAsync(WorkOrderStatus status, CancellationToken ct)
    {
        if (status is WorkOrderStatus.Completed or WorkOrderStatus.Closed or WorkOrderStatus.Cancelled)
            throw new InvalidOperationException("Tamamlanmış/kapatılmış/iptal edilmiş iş emrinin reçetesi düzeltilemez.");
        if (status != WorkOrderStatus.InProgress) return;

        var allowed = await _parameters.GetBoolAsync(
            ProductionParameters.FormCode, ProductionParameters.AllowStartedWoRecipeEditKey, ct) ?? false;
        if (!allowed)
            throw new InvalidOperationException(
                "Üretimi başlamış iş emrinin reçetesi düzeltilemez. "
                + "(Şirket Parametreleri → Üretim → 'Başlamış iş emrinde reçete düzeltme' açılırsa serbest bırakılır.)");
    }

    /// <summary>İş emrinin reçete seçenekleri: baz + versiyonlar + mevcut seçim.</summary>
    public async Task<(int? SelectedBomId, IReadOnlyCollection<BomVersionSummaryDto> Options)> GetBomOptionsAsync(
        int workOrderId, CancellationToken ct)
    {
        var wo = await _workOrders.GetAsync(workOrderId, ct)
            ?? throw new InvalidOperationException("Iş emri bulunamadi.");
        var options = await _logisticsConfig.GetBomVersionsAsync(wo.ItemId, wo.ConfigId, ct);
        var selected = await _workOrders.GetBomIdAsync(workOrderId, ct);
        return (selected, options);
    }

    /// <summary>
    /// İş emrinin reçete seçimini değiştirir. NULL = baz reçete (canlı takip).
    /// Patlatılmış bileşenler DEĞİŞMEZ — yeni seçim bir sonraki "Patlat"ta uygulanır.
    /// </summary>
    public async Task SetBomAsync(int workOrderId, int? bomId, int? userId, CancellationToken ct)
    {
        var wo = await _workOrders.GetAsync(workOrderId, ct)
            ?? throw new InvalidOperationException("Iş emri bulunamadi.");
        await EnsureRecipeEditableAsync(wo.Status, ct);

        if (bomId is > 0)
        {
            var bom = await _logisticsConfig.GetBOMByIdWithNamesAsync(bomId.Value, ct);
            if (bom is null || bom.ItemId != wo.ItemId)
                throw new InvalidOperationException("Seçilen reçete bu iş emrinin mamulüne ait değil veya artık mevcut değil.");
        }
        await _workOrders.SetBomAsync(workOrderId, bomId is > 0 ? bomId : null, userId, ct);
        _audit?.LogChanges("WorkOrder", workOrderId, wo.OrderNumber,
            new[] { new CalibraHub.Application.Auditing.AuditFieldChange(
                "BomId", "Reçete Seçimi", null, bomId is > 0 ? $"#{bomId}" : "Baz reçete") });
    }

    /// <summary>İş emrine tekil bileşen ekler (reçeteden bağımsız özelleştirme).</summary>
    public async Task<int> AddComponentAsync(int workOrderId, int itemId, int? configId,
        decimal quantity, decimal scrapRate, string? notes, CancellationToken ct)
    {
        var wo = await _workOrders.GetAsync(workOrderId, ct)
            ?? throw new InvalidOperationException("Iş emri bulunamadi.");
        await EnsureRecipeEditableAsync(wo.Status, ct);
        if (itemId <= 0) throw new InvalidOperationException("Bileşen malzemesi seçilmelidir.");
        if (quantity <= 0) throw new InvalidOperationException("Miktar sıfırdan büyük olmalıdır.");
        if (scrapRate < 0) throw new InvalidOperationException("Fire oranı negatif olamaz.");
        if (itemId == wo.ItemId)
            throw new InvalidOperationException("İş emrinin mamulü kendi bileşeni olamaz.");

        var existing = await _workOrderComponents.GetByWorkOrderAsync(workOrderId, ct);
        if (existing.Any(e => e.ItemId == itemId && (e.ConfigId ?? 0) == (configId ?? 0)))
            throw new InvalidOperationException("Bu bileşen iş emrinde zaten var — mevcut satırın miktarını düzenleyin.");

        var newId = await _workOrderComponents.AddAsync(new WorkOrderComponent
        {
            WorkOrderId      = workOrderId,
            ItemId           = itemId,
            ConfigId         = configId,
            RequiredQuantity = quantity,
            IssuedQuantity   = 0m,
            ScrapRate        = scrapRate,
            Notes            = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        }, ct);
        _audit?.LogChanges("WorkOrder", workOrderId, wo.OrderNumber,
            new[] { new CalibraHub.Application.Auditing.AuditFieldChange(
                $"Component[{itemId}]", "Bileşen Eklendi", null, $"#{itemId} x{quantity}") });
        return newId;
    }

    /// <summary>Bileşen miktar/fire/not günceller. Sarf edilenin altına düşürülemez (parametreden bağımsız).</summary>
    public async Task UpdateComponentAsync(int componentId, decimal quantity, decimal scrapRate, string? notes, CancellationToken ct)
    {
        var comp = await _workOrderComponents.GetByIdAsync(componentId, ct)
            ?? throw new InvalidOperationException("Bileşen bulunamadı.");
        var wo = await _workOrders.GetAsync(comp.WorkOrderId, ct)
            ?? throw new InvalidOperationException("Iş emri bulunamadi.");
        await EnsureRecipeEditableAsync(wo.Status, ct);
        if (quantity <= 0) throw new InvalidOperationException("Miktar sıfırdan büyük olmalıdır.");
        if (scrapRate < 0) throw new InvalidOperationException("Fire oranı negatif olamaz.");
        if (quantity < comp.IssuedQuantity)
            throw new InvalidOperationException(
                $"Miktar, sarf edilmiş miktarın ({comp.IssuedQuantity}) altına düşürülemez.");

        await _workOrderComponents.UpdateAsync(componentId, quantity, scrapRate,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), ct);
        if (comp.RequiredQuantity != quantity || comp.ScrapRate != scrapRate)
            _audit?.LogChanges("WorkOrder", comp.WorkOrderId, wo.OrderNumber,
                new[] { new CalibraHub.Application.Auditing.AuditFieldChange(
                    $"Component[{comp.ItemId}]", $"Bileşen — {comp.ItemCode ?? ("#" + comp.ItemId)}",
                    $"x{comp.RequiredQuantity}" + (comp.ScrapRate > 0 ? $" (fire {comp.ScrapRate})" : ""),
                    $"x{quantity}" + (scrapRate > 0 ? $" (fire {scrapRate})" : "")) });
    }

    /// <summary>Bileşen siler. Sarf yapılmış satır SİLİNEMEZ (parametreden bağımsız — mutabakat korunur).</summary>
    public async Task DeleteComponentAsync(int componentId, CancellationToken ct)
    {
        var comp = await _workOrderComponents.GetByIdAsync(componentId, ct)
            ?? throw new InvalidOperationException("Bileşen bulunamadı.");
        var wo = await _workOrders.GetAsync(comp.WorkOrderId, ct)
            ?? throw new InvalidOperationException("Iş emri bulunamadi.");
        await EnsureRecipeEditableAsync(wo.Status, ct);
        if (comp.IssuedQuantity > 0)
            throw new InvalidOperationException(
                "Sarf yapılmış bileşen silinemez — önce sarf hareketleri değerlendirilmelidir.");

        await _workOrderComponents.DeleteAsync(componentId, ct);
        _audit?.LogChanges("WorkOrder", comp.WorkOrderId, wo.OrderNumber,
            new[] { new CalibraHub.Application.Auditing.AuditFieldChange(
                $"Component[{comp.ItemId}]", $"Bileşen — {comp.ItemCode ?? ("#" + comp.ItemId)}",
                $"x{comp.RequiredQuantity}", null) });
    }

    public async Task IssueComponentAsync(IssueWorkOrderComponentRequest request, CancellationToken ct)
    {
        if (request.WorkOrderComponentId <= 0) throw new ArgumentException("Bileşen kaydı zorunlu.");
        if (request.Quantity <= 0) throw new ArgumentException("Miktar 0'dan büyük olmalı.");
        if (request.OperatorPersonnelId <= 0) throw new ArgumentException("Operatör (Personnel) zorunlu.");

        // İşlem logu için eski durumu mutasyondan ÖNCE oku (yalnızca audit amaçlı —
        // okunamazsa sarf işlemi yine de devam eder, log sessizce atlanır).
        WorkOrderComponentDto? before = null;
        if (_audit is not null)
        {
            try { before = await _workOrderComponents.GetByIdAsync(request.WorkOrderComponentId, ct); }
            catch { /* audit için eski durum okunamadı */ }
        }

        await _workOrderComponents.IssueAsync(request.WorkOrderComponentId, request.Quantity, request.OperatorPersonnelId, ct);

        // ShopFloor "Malzeme Sarf Et" — İş Emri'nin ("WorkOrder") kendi Değişiklik Geçmişi
        // zaman çizelgesine yazılır (WorkOrderEdit ekranındaki _AuditTrailHost ile aynı kova).
        if (_audit is not null && before is not null)
        {
            try
            {
                string? workOrderNumber = null;
                try { workOrderNumber = (await _workOrders.GetAsync(before.WorkOrderId, ct))?.OrderNumber; }
                catch { /* başlık için WO okunamadı — title null kalır, log yine yazılır */ }

                var itemLabel = before.ItemCode ?? before.ItemName ?? ("#" + before.ItemId);
                var newIssued = before.IssuedQuantity + request.Quantity;
                _audit.LogChanges("WorkOrder", before.WorkOrderId, workOrderNumber,
                    [new AuditFieldChange($"Component[{before.Id}].IssuedQuantity", $"Malzeme Sarfı — {itemLabel}",
                        AuditDiff.Normalize(before.IssuedQuantity), AuditDiff.Normalize(newIssued))],
                    detail: $"Malzeme sarfı — {itemLabel} · +{AuditDiff.Normalize(request.Quantity)} · " +
                            $"Operatör Personel #{request.OperatorPersonnelId}");
            }
            catch { /* audit yazımı sarf işlemini asla bozmaz */ }
        }
    }

    /// <summary>
    /// İş Emri ekranında kullanıcının bir bileşenin planlı sarf lokasyonunu (FromLocationId)
    /// elle seçmesi/değiştirmesi (2026-07-31). locationId null gönderilirse override kaldırılır
    /// (sarf motoru IssueWorkOrderConsumptionAsync tekrar WO'nun genel deposuna düşer).
    /// </summary>
    public async Task<(bool ok, string? error)> UpdateComponentLocationAsync(int componentId, int? locationId, int? userId, CancellationToken ct)
    {
        if (componentId <= 0) return (false, "Bileşen kaydı zorunlu.");

        var before = await _workOrderComponents.GetByIdAsync(componentId, ct);
        if (before is null) return (false, "Bileşen bulunamadı.");

        await _workOrderComponents.UpdateFromLocationAsync(componentId, locationId, ct);

        if (_audit is not null)
        {
            try
            {
                string? workOrderNumber = null;
                try { workOrderNumber = (await _workOrders.GetAsync(before.WorkOrderId, ct))?.OrderNumber; }
                catch { /* başlık için WO okunamadı — title null kalır, log yine yazılır */ }

                var itemLabel = before.ItemCode ?? before.ItemName ?? ("#" + before.ItemId);
                _audit.LogChanges("WorkOrder", before.WorkOrderId, workOrderNumber,
                    [new AuditFieldChange($"Component[{before.Id}].FromLocationId", $"Sarf Lokasyonu — {itemLabel}",
                        before.FromLocationCode ?? before.FromLocationId?.ToString(),
                        locationId?.ToString())],
                    detail: $"Sarf lokasyonu değiştirildi — {itemLabel}");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[WorkOrder.UpdateComponentLocation] componentId={ComponentId} audit yazılamadı.", componentId);
            }
        }

        return (true, null);
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
