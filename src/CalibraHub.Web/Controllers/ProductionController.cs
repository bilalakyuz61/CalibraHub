using CalibraHub.Application.Constants;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Common;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Helpers;
using CalibraHub.Web.Models.Production;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static CalibraHub.Web.Helpers.AuditLogActionHelper;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Uretim modulu — Faz 1 cekirdek is emri ekrani.
/// Routes:
///   GET  /Production/WorkOrders        → liste
///   GET  /Production/WorkOrderEdit?id  → master form (yeni veya edit)
///   POST /Production/Create            → JSON (yeni emir)
///   POST /Production/Update/{id}       → JSON (Planned guncelleme)
///   POST /Production/ChangeStatus/{id} → JSON (durum gecisi)
///   POST /Production/Revise/{id}       → JSON (revize akisi)
///   POST /Production/CreateFromSalesLine → JSON (Sales modal cagri noktasi)
///   GET  /Production/EligibleForMerge  → JSON (toplama icin uygun emirler)
///   GET  /Production/AllocatedQuantity → JSON (sipariş satırı acik bakiye)
/// </summary>
[Authorize]
public sealed class ProductionController : Controller
{
    private readonly IWorkOrderService _service;
    private readonly IOperationService _operations;
    private readonly IRoutingService _routings;
    private readonly IOperationMachineTimeService _machineTimes;
    private readonly IWorkOrderOperationService _workOrderOperations;
    // 2026-08-24 — "Üretim Hareketleri" sekmesi: iş emrinin stok satırlarını okur.
    private readonly CalibraHub.Application.Abstractions.Persistence.IWorkOrderRepository _workOrderRepo;
    private readonly CalibraHub.Application.Abstractions.Persistence.IComputedColumnRepository _computedColumns;
    private readonly IPersonnelService _personnel;
    private readonly IWidgetService _widgetService;
    private readonly ILogisticsConfigurationService _logisticsConfig;
    // 2026-05-20 — Faz 1 MVP: saha aktivite log servisi (Durum Değiştir + Hareket Geçmişi).
    private readonly IWorkOrderOperationActivityService _activities;
    // 2026-05-21 — Faz 2: aktivite alt sebep sözlüğü (Arıza → 'Sensör', 'Elektrik' vb.)
    private readonly IActivityReasonService _activityReasons;
    // 2026-05-21 — Faz 3: vardiya tanımı + personel atama (haftalık tekrar pattern)
    private readonly IShiftService _shifts;
    private readonly IShiftAssignmentService _shiftAssignments;
    // 2026-06-12 — ShopFloor PIN lockout: hatalı deneme sayacı + parametre okuma + IsActive=0 lock
    private readonly IPersonnelRepository _personnelRepo;
    private readonly ICompanyParameterService _companyParameters;
    private readonly CalibraHub.Application.Services.ShopFloorLockoutTracker _shopFloorLockout;
    private readonly CalibraHub.Persistence.Database.SqlServerConnectionFactory _connectionFactory;
    private readonly ILogger<ProductionController> _logger;
    private readonly IUserSettingRepository _userSettings;
    // 2026-08-04 — Makine Planlama (Üretim Çizelgeleme) Faz 1 Manuel.
    private readonly IMachineScheduleRepository _machineSchedule;
    // 2026-08-05 — Makine Çalışma Takvimi (haftalık müsaitlik + resmi tatil) Faz 2.
    private readonly IMachineCalendarRepository _machineCalendar;
    // 2026-08-05 — Makine Planlama Faz 3: otomatik çizelgeleme önerisi (forward, sonlu-kapasite motor).
    private readonly IMachineAutoScheduleService _autoSchedule;
    // 2026-08-22 — Kapasite / Yük Raporu (makine doluluk ısı haritası).
    private readonly IMachineCapacityReportService _capacityReport;
    private readonly CalibraHub.Application.Auditing.IAuditTrailService _audit;

    public ProductionController(
        IWorkOrderService service,
        IOperationService operations,
        IRoutingService routings,
        IOperationMachineTimeService machineTimes,
        IWorkOrderOperationService workOrderOperations,
        CalibraHub.Application.Abstractions.Persistence.IWorkOrderRepository workOrderRepo,
        CalibraHub.Application.Abstractions.Persistence.IComputedColumnRepository computedColumns,
        IPersonnelService personnel,
        IWidgetService widgetService,
        ILogisticsConfigurationService logisticsConfig,
        IWorkOrderOperationActivityService activities,
        IActivityReasonService activityReasons,
        IShiftService shifts,
        IShiftAssignmentService shiftAssignments,
        IPersonnelRepository personnelRepo,
        ICompanyParameterService companyParameters,
        CalibraHub.Application.Services.ShopFloorLockoutTracker shopFloorLockout,
        CalibraHub.Persistence.Database.SqlServerConnectionFactory connectionFactory,
        IMachineScheduleRepository machineSchedule,
        IMachineCalendarRepository machineCalendar,
        IMachineAutoScheduleService autoSchedule,
        IMachineCapacityReportService capacityReport,
        CalibraHub.Application.Auditing.IAuditTrailService audit,
        ILogger<ProductionController> logger,
        IUserSettingRepository userSettings)
    {
        _service = service;
        _operations = operations;
        _routings = routings;
        _machineTimes = machineTimes;
        _workOrderOperations = workOrderOperations;
        _workOrderRepo = workOrderRepo;
        _computedColumns = computedColumns;
        _personnel = personnel;
        _widgetService = widgetService;
        _logisticsConfig = logisticsConfig;
        _activities = activities;
        _activityReasons = activityReasons;
        _shifts = shifts;
        _shiftAssignments = shiftAssignments;
        _personnelRepo = personnelRepo;
        _companyParameters = companyParameters;
        _shopFloorLockout = shopFloorLockout;
        _connectionFactory = connectionFactory;
        _machineSchedule = machineSchedule;
        _machineCalendar = machineCalendar;
        _autoSchedule = autoSchedule;
        _capacityReport = capacityReport;
        _audit = audit;
        _logger = logger;
        _userSettings = userSettings;
    }

    private static string BlockTypeLabel(byte t) => t switch
    {
        2 => "Hazırlık",
        3 => "Bakım",
        4 => "Duruş",
        _ => "Üretim",
    };

    private static string BlockStatusLabel(byte s) => s switch
    {
        2 => "Kilitli",
        3 => "Onaylı",
        _ => "Planlı",
    };

    private int ResolveCurrentCompanyIdSafe()
    {
        try { return _connectionFactory.ResolveCurrentCompanyId(); }
        catch { return 0; }
    }

    private async Task<int> GetShopFloorMaxPinAttemptsAsync(CancellationToken ct)
    {
        try
        {
            var p = await _companyParameters.ListAsync("PRODUCTION", ct);
            var raw = p.FirstOrDefault(x => x.ParamKey == "SHOPFLOOR_MAX_PIN_ATTEMPTS")?.ParamValue;
            if (int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture, out var v)
                && v >= 0 && v <= 50)
                return v;
        }
        catch { /* parametre yoksa default */ }
        return 5;
    }

    // Yetki kapisi (2026-08-24): sayfa ve veri ucu daha once HIC gate edilmiyordu — oturum
    // acan herkes is emri listesini gorebiliyordu. Menu (MenuDefinition) ve mobil API
    // (MobileProductionApiController) zaten FormCodes.WorkOrders'a bakiyordu; yani menude
    // gizli ama uc acikti. Fonksiyon testlerinin yetki grubu bu acigi ortaya cikardi.
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrders)]
    public async Task<IActionResult> WorkOrders(string? status, CancellationToken ct)
    {
        WorkOrderStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<WorkOrderStatus>(status, true, out var s))
            filter = s;
        var boardConfig = await BuildWorkOrdersBoardConfigAsync(filter, ct);
        ViewBag.StatusFilter = filter;
        return View(new WorkOrdersViewModel { BoardConfig = boardConfig });
    }

    // ════════════════════════════════════════════════════════════════
    // BuildWorkOrdersBoardConfigAsync — SmartBoard server-side config.
    // Sales/Documents pattern'i: sistem widget'lari + admin tanimli dinamik
    // widget'lar (WORK_ORDER_EDIT form code). Aksiyonlar: Duzenle (kart click)
    // + Iptal (status change) + Sil (DELETE).
    // ════════════════════════════════════════════════════════════════
    private async Task<object> BuildWorkOrdersBoardConfigAsync(WorkOrderStatus? statusFilter, CancellationToken ct)
    {
        var orders = await _service.ListAsync(statusFilter, ct);
        // Iptal/Kapali emirler default listede gizli — kullanici "Cancelled/Closed" filtresi
        // ile aciktan istemedikce kart akisini bulanik gostermesinler.
        if (!statusFilter.HasValue)
        {
            orders = orders
                .Where(o => o.Status != WorkOrderStatus.Cancelled && o.Status != WorkOrderStatus.Closed)
                .ToArray();
        }
        var trCulture = CultureInfo.GetCultureInfo("tr-TR");

        // Master widget şablonu — admin SmartBoardConfigPanel için
        var schema = await _widgetService.GetFormSchemaByCodeAsync("WORK_ORDER_EDIT", ct);
        var masterWidgets = SmartBoardFilterHelpers.BuildAdminFormWidgets(schema);
        // Sistem widget'lari — Standart Alanlar grubunda
        var statusOptions = SmartBoardFilterHelpers.ToOptionsList(new[]
        {
            WorkOrderStatusLabel(WorkOrderStatus.Planned),
            WorkOrderStatusLabel(WorkOrderStatus.Released),
            WorkOrderStatusLabel(WorkOrderStatus.InProgress),
            WorkOrderStatusLabel(WorkOrderStatus.Completed),
            WorkOrderStatusLabel(WorkOrderStatus.Closed),
            WorkOrderStatusLabel(WorkOrderStatus.Cancelled),
        });
        var priorityOptions = SmartBoardFilterHelpers.ToOptionsList(new[]
        {
            WorkOrderPriorityLabel(WorkOrderPriority.Low),
            WorkOrderPriorityLabel(WorkOrderPriority.Medium),
            WorkOrderPriorityLabel(WorkOrderPriority.High),
        });
        masterWidgets.Add(SmartBoardFilterHelpers.MakeOptionsWidget("w_status",       "Durum",        statusOptions));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget   ("w_planned_qty",  "Planlanan",    "numeric"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget   ("w_produced_qty", "Üretilen",     "numeric"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeOptionsWidget("w_priority",     "Öncelik",      priorityOptions));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget   ("w_order_date",   "Tarih",        "date"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget   ("w_planned_end",  "Plan Bitiş",   "date"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget   ("w_assigned",     "Sorumlu",      "text"));

        // Batch widget değerleri
        var recordIds = orders.Select(o => o.Id.ToString()).ToArray();
        var batchWidgets = masterWidgets.Count > 0 && recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("WORK_ORDER_EDIT", recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        // Hesaplanan kolonlar — anahtar WorkOrder.Id. Degerler YALNIZ listedeki emirler
        // icin okunur; is emri numarasi Document.DocumentNumber'da durdugu icin anahtar
        // olarak NUMARA degil Id kullanilir (numara gosterim icindir, kimlik degil).
        var calc = await new CalibraHub.Web.Infrastructure.ComputedColumnBinder(_computedColumns, _logger)
            .LoadAsync(CalibraHub.Application.Contracts.ComputedColumnEntities.WorkOrder,
                       "production-workorders", orders.Select(o => o.Id).ToArray(), ct);
        masterWidgets.AddRange(calc.MasterWidgets());

        var entities = new List<object>();
        foreach (var o in orders)
        {
            var widgets = new List<object>();
            // Sistem widget'ları
            widgets.Add(new { id = "w_status", type = "data", dataType = "text",
                label = "Durum", value = WorkOrderStatusLabel(o.Status), detail = (string?)null,
                color = WorkOrderStatusColor(o.Status) });
            widgets.Add(new { id = "w_planned_qty", type = "data", dataType = "numeric",
                label = "Planlanan", value = o.PlannedQuantity.ToString("N2", trCulture), detail = o.UnitCode ?? "",
                color = "indigo" });
            widgets.Add(new { id = "w_produced_qty", type = "data", dataType = "numeric",
                label = "Üretilen", value = o.ProducedQuantity.ToString("N2", trCulture), detail = o.UnitCode ?? "",
                color = "emerald" });
            widgets.Add(new { id = "w_priority", type = "data", dataType = "text",
                label = "Öncelik", value = WorkOrderPriorityLabel(o.Priority), detail = (string?)null,
                color = o.Priority == WorkOrderPriority.High ? "rose" : "slate" });
            widgets.Add(new { id = "w_order_date", type = "data", dataType = "date",
                label = "Tarih", value = o.OrderDate.ToLocalTime().ToString("dd.MM.yyyy", trCulture), detail = (string?)null,
                color = "slate" });
            if (o.PlannedEndDate.HasValue)
            {
                var future = o.PlannedEndDate.Value.Date >= DateTime.Today;
                widgets.Add(new { id = "w_planned_end", type = "data", dataType = "date",
                    label = "Plan Bitiş", value = o.PlannedEndDate.Value.ToString("dd.MM.yyyy", trCulture),
                    detail = (string?)null, color = future ? "emerald" : "rose" });
            }
            // Sorumlu — once Personnel adi (yeni atama), yoksa User adi (legacy fallback)
            var assignedDisplay = o.AssignedPersonnelName ?? o.AssignedUserName;
            if (!string.IsNullOrWhiteSpace(assignedDisplay))
            {
                widgets.Add(new { id = "w_assigned", type = "data", dataType = "text",
                    label = "Sorumlu", value = assignedDisplay, detail = (string?)null, color = "slate" });
            }

            // Dinamik widget'lar (WidgetTra)
            var recordId = o.Id.ToString();
            if (batchWidgets.TryGetValue(recordId, out var dtos))
            {
                foreach (var w in dtos)
                {
                    widgets.Add(new {
                        id = w.WidgetId,
                        type = "data",
                        dataType = w.DataType.ToLowerInvariant(),
                        label = w.Label,
                        value = w.Value,
                        isPlainField = w.IsPlainField,
                    });
                }
            }

            var titleSuffix = o.RevisionNo > 0 ? $" • Rev {o.RevisionNo}" : "";
            widgets.AddRange(calc.CellsFor(o.Id));

            entities.Add(new
            {
                id = o.Id,
                title = string.IsNullOrWhiteSpace(o.ItemName) ? (o.ItemCode ?? "(mamul yok)") : o.ItemName,
                subtitle = (o.OrderNumber ?? "") + titleSuffix,
                description = string.Empty,
                imageUrl = (string?)null,
                statusBadge = (object?)null,
                widgets,
                primaryAction = new
                {
                    label = "Düzenle",
                    icon = "Edit",
                    color = "amber",
                    url = $"/Production/WorkOrderEdit?id={o.Id}",
                    hideButton = true,
                },
                secondaryAction = new
                {
                    label = "İptal Et",
                    icon = "Trash2",
                    apiUrl = $"/Production/ChangeStatus/{o.Id}",
                    apiMethod = "POST",
                    apiBody = new { workOrderId = o.Id, newStatus = (int)WorkOrderStatus.Cancelled },
                    confirm = $"Bu iş emrini iptal etmek istediğinize emin misiniz? ({o.OrderNumber})",
                },
                extraActions = new object[] { BuildAuditLogAction("WorkOrder", o.Id, "WORK_ORDER_EDIT") },
            });
        }

        return new
        {
            boardKey = "production-workorders",
            title = "Üretim İş Emirleri",
            subtitle = $"{entities.Count} emir",
            icon = "ClipboardList",
            iconColor = "indigo",
            // In-place refresh — secondaryAction sonrasi SmartBoard board'u yeniden ceker.
            refreshUrl = "/Production/WorkOrdersBoardConfig",
            searchPlaceholder = "Hızlı ara... (emir no, mamul)",
            emptyText = "Henüz iş emri yok",
            actions = new object[]
            {
                new
                {
                    id = "new",
                    label = "Yeni İş Emri",
                    icon = "Plus",
                    variant = "primary",
                    url = "/Production/WorkOrderEdit",
                },
            },
            masterWidgets,
            entities,
        };
    }

    // In-place refresh — kart aksiyonu (Iptal Et / Status change) sonrasi tum config'i tekrar ceker.
    // Sayfayla AYNI kapi: veri ucu acik kalirsa sayfayi kapatmanin anlami olmaz.
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrders)]
    public async Task<IActionResult> WorkOrdersBoardConfig(string? status, CancellationToken ct)
    {
        WorkOrderStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<WorkOrderStatus>(status, true, out var s))
            filter = s;
        var board = await BuildWorkOrdersBoardConfigAsync(filter, ct);
        return Json(board);
    }

    private static string WorkOrderStatusLabel(WorkOrderStatus s) => s switch
    {
        WorkOrderStatus.Planned => "Taslak",
        WorkOrderStatus.Released => "Yayımlandı",
        WorkOrderStatus.InProgress => "Devam ediyor",
        WorkOrderStatus.Completed => "Tamamlandı",
        WorkOrderStatus.Closed => "Kapatıldı",
        WorkOrderStatus.Cancelled => "İptal",
        _ => s.ToString()
    };

    private static string WorkOrderStatusColor(WorkOrderStatus s) => s switch
    {
        WorkOrderStatus.Planned => "indigo",
        WorkOrderStatus.Released => "amber",
        WorkOrderStatus.InProgress => "violet",
        WorkOrderStatus.Completed => "emerald",
        WorkOrderStatus.Closed => "slate",
        WorkOrderStatus.Cancelled => "rose",
        _ => "slate"
    };

    private static string WorkOrderPriorityLabel(WorkOrderPriority p) => p switch
    {
        WorkOrderPriority.Low => "Düşük",
        WorkOrderPriority.Medium => "Normal",
        WorkOrderPriority.High => "Yüksek",
        _ => p.ToString()
    };

    [HttpGet]
    public async Task<IActionResult> WorkOrderEdit(int? id, CancellationToken ct)
    {
        if (id is null or 0)
        {
            return View((WorkOrderDto?)null);
        }
        var dto = await _service.GetAsync(id.Value, ct);
        if (dto is null) return NotFound();
        // Üretim sarfı modalı kalem grid'i — STOCK_OUT kolon seti (lookup + kombinasyon +
        // seri-pick + lot + miktar) aynen yeniden kullanılır (2026-07-10 üretim sarfı).
        ViewData["ConsumptionGridConfigJson"] = System.Text.Json.JsonSerializer.Serialize(
            WarehouseController.BuildLineGridConfig("STOCK_OUT", null, await GetLineViewModeAsync(ct)),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });
        return View(dto);
    }

    /// <summary>
    /// Üretim sarfı (2026-07-10) — üretilen miktara göre bileşen sarfı: reçete önerisi +
    /// serbest satır; lot/seri kuralları stok çıkışıyla aynı (sunucu tarafı zorunlu).
    /// </summary>
    [HttpPost("Production/WorkOrder/IssueConsumptionJson")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> IssueConsumptionJson(
        [FromBody] WorkOrderConsumptionRequest req,
        [FromServices] IStockDocRepository stockDocRepo,
        CancellationToken ct)
    {
        if (req is null || req.WorkOrderId <= 0)
            return Json(new { ok = false, error = "İş emri belirtilmedi." });
        try
        {
            var lines = await stockDocRepo.IssueWorkOrderConsumptionAsync(req, CurrentUserId(), ct);
            return Json(new { ok = true, lines });
        }
        catch (CalibraHub.Domain.Exceptions.NegativeBalanceException nbex)
        {
            return Json(new { ok = false, error = nbex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // İş kuralı mesajları (lot/seri zorunluluğu, bakiye, durum) kullanıcıya aynen gösterilir
            return Json(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.IssueConsumption] workOrderId={WorkOrderId} üretim sarfı yapılamadı.", req.WorkOrderId);
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> Create([FromBody] CreateWorkOrderRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _service.CreateAsync(req, ct);
            return Json(new { ok = true, id });
        }
        catch (ArgumentException ex)
        {
            // Doğrulama mesajları (miktar/mamul zorunlu, Malzeme Belge Kilidi vb.) — kullanıcıya
            // aynen gösterilir; jenerik catch bu mesajı gizlerdi (2026-07-25 fix).
            return Json(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.Create] iş emri oluşturulamadı. ItemId={ItemId}", req?.ItemId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateWorkOrderRequest req, CancellationToken ct)
    {
        try
        {
            await _service.UpdateAsync(id, req, ct);
            return Json(new { ok = true });
        }
        catch (ArgumentException ex)      { return Json(new { ok = false, error = ex.Message }); }
        catch (InvalidOperationException ex) { return Json(new { ok = false, error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.Update] id={Id} güncellenemedi.", id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> ChangeStatus(int id, [FromBody] ChangeWorkOrderStatusRequest req, CancellationToken ct)
    {
        try
        {
            await _service.ChangeStatusAsync(id, req.NewStatus, ct);
            return Json(new { ok = true });
        }
        catch (ArgumentException ex)      { return Json(new { ok = false, error = ex.Message }); }
        catch (InvalidOperationException ex) { return Json(new { ok = false, error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.ChangeStatus] id={Id} newStatus={NewStatus} durum değiştirilemedi.", id, req?.NewStatus);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> Revise(int id, CancellationToken ct)
    {
        try
        {
            var newId = await _service.ReviseAsync(id, ct);
            return Json(new { ok = true, id = newId });
        }
        catch (ArgumentException ex)      { return Json(new { ok = false, error = ex.Message }); }
        catch (InvalidOperationException ex) { return Json(new { ok = false, error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.Revise] id={Id} revize oluşturulamadı.", id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> CreateFromSalesLine([FromBody] CreateWorkOrderFromSalesLineRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _service.CreateFromSalesLineAsync(req, ct);
            return Json(new { ok = true, id });
        }
        catch (InvalidOperationException ex)
        {
            // İş kuralı mesajları (kaynak satır/kalan miktar/mamul eşleşmesi, Malzeme Belge
            // Kilidi vb.) — kullanıcıya aynen gösterilir; jenerik catch bu mesajı gizlerdi
            // (2026-07-25 fix).
            return Json(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.CreateFromSalesLine] satış satırından iş emri oluşturulamadı. SourceLineId={SourceLineId}", req?.SourceLineId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> EligibleForMerge(int itemId, int? configId, CancellationToken ct)
    {
        var list = await _service.ListEligibleForMergeAsync(itemId, configId, ct);
        return Json(list);
    }

    [HttpGet]
    public async Task<IActionResult> AllocatedQuantity(int sourceLineId, CancellationToken ct)
    {
        var qty = await _service.GetAllocatedQuantityForLineAsync(sourceLineId, ct);
        return Json(new { allocated = qty });
    }

    // ── Operasyon Tanımlamaları ──────────────────────────────────────────────
    // GET  /Production/Operations            → Razor liste + form ekranı
    // GET  /Production/OperationsList        → JSON liste (admin grid)
    // GET  /Production/Operation/{id}        → JSON tekil
    // POST /Production/SaveOperation         → JSON (id=0 yeni, id>0 update)
    // POST /Production/DeleteOperation/{id}  → JSON
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.OperationEdit)]
    public async Task<IActionResult> Operations(CancellationToken ct)
    {
        var boardConfig = await BuildOperationsBoardConfigAsync(ct);
        return View(new OperationsViewModel { BoardConfig = boardConfig });
    }

    [HttpGet("/Production/OperationsBoardConfig")]
    public async Task<IActionResult> OperationsBoardConfig(CancellationToken ct)
    {
        var boardConfig = await BuildOperationsBoardConfigAsync(ct);
        return Json(boardConfig);
    }

    private async Task<object> BuildOperationsGridConfigAsync(CancellationToken ct)
    {
        var ops = await _operations.ListAsync(includeInactive: true, ct);
        return new
        {
            operations = ops.Select(o => new
            {
                id               = o.Id,
                code             = o.Code,
                name             = o.Name,
                description      = o.Description,
                standardDuration = o.StandardDuration,
                durationUnit     = (int)o.DurationUnit,
                hourlyRate       = o.HourlyRate,
                sortOrder        = o.SortOrder,
                isActive         = o.IsActive,
            }),
            urls = new
            {
                save    = "/Production/SaveOperation",
                delete  = "/Production/DeleteOperation",
                refresh = "/Production/OperationsGridConfig",
            },
        };
    }

    // ════════════════════════════════════════════════════════════════
    // BuildOperationsBoardConfigAsync — operasyon kartlari icin SmartBoard
    // config. Sistem widget'lari (status, sure, ucret) + admin tanimli
    // dinamik widget'lar (OPERATION_EDIT form code).
    // ════════════════════════════════════════════════════════════════
    private async Task<object> BuildOperationsBoardConfigAsync(CancellationToken ct)
    {
        var ops = await _operations.ListAsync(includeInactive: true, ct);
        var trCulture = CultureInfo.GetCultureInfo("tr-TR");

        var schema = await _widgetService.GetFormSchemaByCodeAsync("OPERATION_EDIT", ct);
        var masterWidgets = SmartBoardFilterHelpers.BuildAdminFormWidgets(schema);
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget("w_active",   "Durum",         "boolean"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget("w_duration", "Std. Süre",     "numeric"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget("w_rate",     "Saatlik Ücret", "numeric"));

        var recordIds = ops.Select(o => o.Id.ToString()).ToArray();
        var batchWidgets = masterWidgets.Count > 0 && recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("OPERATION_EDIT", recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        // SmartBoardBuilder ile yeniden yazildi (rapor §2.5) — eski 100+ satir anonymous type
        // boilerplate fluent API ile ~50 satira indi.
        return CalibraHub.Application.SmartBoard.SmartBoard.For(ops)
            .WithBoardKey("production-operations")
            .WithTitle("Operasyon Tanımlamaları", subtitle: $"{ops.Count} operasyon")
            .WithIcon("Hammer", "indigo")
            .WithRefreshUrl("/Production/OperationsBoardConfig")
            .WithSearchPlaceholder("Hızlı ara... (kod, ad)")
            .WithEmptyText("Henüz operasyon tanımlanmamış")
            .AddHeaderAction("new", "Yeni Operasyon", "Plus", "/Production/OperationEdit")
            .WithMasterWidgets(masterWidgets)
            .MapEntities(o =>
            {
                var eb = CalibraHub.Application.SmartBoard.SmartBoardEntity
                    .For(o.Id, o.Name, subtitle: o.Code)
                    .WithDescription(o.Description ?? string.Empty)
                    .AddStatusWidget("w_active", "Durum", o.IsActive);

                if (o.StandardDuration.HasValue)
                {
                    var unit = o.DurationUnit == DurationUnit.Hour ? "saat" : "dk";
                    eb.AddNumericWidget("w_duration", "Std. Süre",
                        o.StandardDuration.Value.ToString("N2", trCulture), detail: unit, color: "indigo");
                }
                if (o.HourlyRate.HasValue)
                {
                    eb.AddNumericWidget("w_rate", "Saatlik Ücret",
                        o.HourlyRate.Value.ToString("N2", trCulture), detail: "TL/saat", color: "blue");
                }

                if (batchWidgets.TryGetValue(o.Id.ToString(), out var dtos))
                {
                    eb.AppendWidgets(dtos.Select(w => (object)new
                    {
                        id           = w.WidgetId,
                        type         = "data",
                        dataType     = w.DataType.ToLowerInvariant(),
                        label        = w.Label,
                        value        = w.Value,
                        isPlainField = w.IsPlainField,
                    }));
                }

                return eb.WithEditAndDelete(
                    editUrl:       $"/Production/OperationEdit?id={o.Id}",
                    deleteApiUrl:  $"/Production/DeleteOperation/{o.Id}",
                    deleteConfirm: $"Bu operasyonu silmek istediğinize emin misiniz? ({o.Code})");
            })
            .Build();
    }

    [HttpGet]
    public async Task<IActionResult> OperationsList(bool includeInactive, CancellationToken ct)
    {
        var list = await _operations.ListAsync(includeInactive, ct);
        return Json(list);
    }

    [HttpGet]
    public async Task<IActionResult> Operation(int id, CancellationToken ct)
    {
        var dto = await _operations.GetAsync(id, ct);
        if (dto is null) return NotFound();
        return Json(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.OperationEdit)]
    public async Task<IActionResult> SaveOperation([FromBody] SaveOperationRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _operations.SaveAsync(req, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Operation.Save] id={Id} kaydedilemedi.", req?.Id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.OperationEdit)]
    public async Task<IActionResult> DeleteOperation(int id, CancellationToken ct)
    {
        try
        {
            await _operations.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (ArgumentException ex)
        {
            // OperationService.DeleteAsync kullanim guard'i — kullaniciya oldugu gibi gosterilir.
            return Json(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            // Genel FK-ihlali guvenlik agi (CLAUDE.md "sessiz kirik" #1) — guard'in
            // yakalamadigi bir FK varsa (ornegin ileride eklenecek yeni referans) yine
            // anlamli mesaj doner, "Islem sirasinda bir hata olustu." jenerigine dusmez.
            if (SqlExceptionMessages.TryHandle(ex, _logger, $"ProductionController.DeleteOperation id={id}", out var friendly))
                return Json(new { ok = false, error = friendly });

            _logger.LogError(ex, "[Operation.Delete] id={Id} silinemedi.", id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ── Operation Detay (sol-tab: Genel / Rota / Makine Eşleştirme) ───────────
    [HttpGet]
    public async Task<IActionResult> OperationEdit(int? id, CancellationToken ct)
    {
        OperationDto? dto = null;
        if (id.HasValue && id.Value > 0)
        {
            dto = await _operations.GetAsync(id.Value, ct);
            if (dto is null) return NotFound();
        }
        return View(dto);
    }

    // ── Routing CRUD ekranı ──────────────────────────────────────────────────
    // GET  /Production/Routings                  → RoutingTree view
    // GET  /Production/RoutingTreeConfig         → JSON (in-place refresh)
    // POST /Production/RoutingToggle?id=&enabled=→ JSON (aktif/pasif toggle)
    // GET  /Production/RoutingEdit?id=           → legacy master-detail form (bağlantısız)
    // GET  /Production/RoutingsList?itemId=      → JSON liste (filtreli)
    // GET  /Production/Routing/{id}              → JSON tekil (header + operations)
    // POST /Production/SaveRouting               → JSON (id=0 yeni, id>0 update — header + operations)
    // POST /Production/DeleteRouting/{id}        → JSON
    // GET  /Production/RoutingItemMaps?routingId → JSON mamul eşleştirme listesi
    // POST /Production/AddRoutingItemMap         → JSON ekle
    // POST /Production/DeleteRoutingItemMap      → JSON sil
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.RoutingEdit)]
    public async Task<IActionResult> Routings(CancellationToken ct)
    {
        var treeConfig = await BuildRoutingTreeConfigAsync(ct);
        return View(new RoutingsViewModel { BoardConfig = treeConfig });
    }

    [HttpGet("/Production/RoutingTreeConfig")]
    public async Task<IActionResult> RoutingTreeConfig(CancellationToken ct)
    {
        var treeConfig = await BuildRoutingTreeConfigAsync(ct);
        return Json(treeConfig);
    }

    private async Task<object> BuildRoutingTreeConfigAsync(CancellationToken ct)
    {
        var withOps = await _routings.GetAllWithOperationsAsync(ct);

        // ── Machine lookup (operation row uzerinde gostermek icin) ─────
        var machines = await _logisticsConfig.GetMachinesAsync(ct);
        var machineById = machines.ToDictionary(m => m.Id, m => new
        {
            id = m.Id,
            code = m.Code,
            name = string.IsNullOrWhiteSpace(m.Name) ? m.Code : m.Name,
        });

        // ── Item lookup (rota → mamul eslestirme icin) ─────────────────
        var items = await _logisticsConfig.GetItemsForLookupAsync(ct);
        var itemById = items.ToDictionary(i => i.Id, i => new
        {
            id = i.Id,
            code = (i.Code ?? string.Empty).Trim(),
            name = i.Name ?? string.Empty,
        });

        // ── Routing widget şeması (ROUTING_EDIT) ──────────────────────
        var routingMasterWidgets = new List<object>();
        var routingSchema = await _widgetService.GetFormSchemaByCodeAsync("ROUTING_EDIT", ct);
        if (routingSchema != null)
        {
            foreach (var w in routingSchema.Widgets.Where(w => w.IsActive
                && !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(w.DataType, "grid",  StringComparison.OrdinalIgnoreCase)))
            {
                routingMasterWidgets.Add(new
                {
                    id           = w.WidgetCode,
                    dbId         = w.Id,
                    isPlainField = w.IsPlainField,
                    type         = "data",
                    dataType     = w.DataType.ToLowerInvariant(),
                    label        = w.Label,
                });
            }
        }

        var routingIds = withOps.Select(r => r.Header.Id.ToString()).ToArray();
        var routingBatchWidgets = routingMasterWidgets.Count > 0 && routingIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("ROUTING_EDIT", routingIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        // ── Routing operation widget şeması (ROUTING_OPERATION_EDIT) ──
        var opMasterWidgets = new List<object>();
        var opSchema = await _widgetService.GetFormSchemaByCodeAsync("ROUTING_OPERATION_EDIT", ct);
        if (opSchema != null)
        {
            foreach (var w in opSchema.Widgets.Where(w => w.IsActive
                && !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(w.DataType, "grid",  StringComparison.OrdinalIgnoreCase)))
            {
                opMasterWidgets.Add(new
                {
                    id           = w.WidgetCode,
                    dbId         = w.Id,
                    isPlainField = w.IsPlainField,
                    type         = "data",
                    dataType     = w.DataType.ToLowerInvariant(),
                    label        = w.Label,
                });
            }
        }

        var allOpIds = withOps.SelectMany(r => r.Operations).Select(o => o.Id.ToString()).ToArray();
        var opBatchWidgets = opMasterWidgets.Count > 0 && allOpIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("ROUTING_OPERATION_EDIT", allOpIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        static List<object> BuildDynamicWidgets(string id, IReadOnlyDictionary<string, IReadOnlyCollection<WidgetRenderDto>> batch)
        {
            var list = new List<object>();
            if (batch.TryGetValue(id, out var dtos))
            {
                foreach (var w in dtos)
                {
                    list.Add(new
                    {
                        id = w.WidgetId,
                        type = "data",
                        dataType = w.DataType.ToLowerInvariant(),
                        label = w.Label,
                        value = w.Value,
                        isPlainField = w.IsPlainField,
                    });
                }
            }
            return list;
        }

        return new
        {
            routings = withOps.Select(r => new
            {
                id          = r.Header.Id,
                code        = r.Header.Code,
                name        = r.Header.Name,
                description = r.Header.Description,
                isActive    = r.Header.IsActive,
                itemId      = r.Header.ItemId,
                itemCode    = r.Header.ItemId.HasValue && itemById.TryGetValue(r.Header.ItemId.Value, out var itm) ? itm.code : null,
                itemName    = r.Header.ItemId.HasValue && itemById.TryGetValue(r.Header.ItemId.Value, out var itn) ? itn.name : null,
                widgets     = BuildDynamicWidgets(r.Header.Id.ToString(), routingBatchWidgets),
                operations  = r.Operations.Select(o => new
                {
                    id              = o.Id,
                    routingId       = o.RoutingId,
                    sequence        = o.Sequence,
                    operationId     = o.OperationId,
                    operationCode   = o.OperationCode,
                    operationName   = o.OperationName,
                    machineId       = o.MachineId,
                    machineCode     = o.MachineId.HasValue && machineById.TryGetValue(o.MachineId.Value, out var mc) ? mc.code : null,
                    machineName     = o.MachineId.HasValue && machineById.TryGetValue(o.MachineId.Value, out var mn) ? mn.name : null,
                    overrideDuration= o.OverrideDuration,
                    durationUnit    = (int)o.DurationUnit,
                    notes           = o.Notes,
                    widgets         = BuildDynamicWidgets(o.Id.ToString(), opBatchWidgets),
                }),
            }),
            routingMasterWidgets,
            opMasterWidgets,
            routingFormCode    = "ROUTING_EDIT",
            opFormCode         = "ROUTING_OPERATION_EDIT",
            urls = new
            {
                save             = "/Production/SaveRouting",
                delete           = "/Production/DeleteRouting",
                toggle           = "/Production/RoutingToggle",
                operationsLookup = "/Production/OperationsList?includeInactive=false",
                machinesLookup   = "/Logistics/GetAllMachines",
                itemsLookup      = "/Logistics/StockLookup",
                refresh          = "/Production/RoutingTreeConfig",
            },
        };
    }

    [HttpPost("/Production/RoutingToggle")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.RoutingEdit)]
    public async Task<IActionResult> RoutingToggle([FromQuery] int id, [FromQuery] bool enabled, CancellationToken ct)
    {
        var dto = await _routings.GetAsync(id, ct);
        if (dto is null) return Json(new { ok = false, error = "Rota bulunamadı" });
        var ops = await _routings.GetOperationsAsync(id, ct);
        var req = new SaveRoutingRequest(
            Id: dto.Id, Code: dto.Code, Name: dto.Name, ItemId: dto.ItemId,
            ConfigId: dto.ConfigId, Description: dto.Description, IsActive: enabled,
            Operations: ops.Select(o => new SaveRoutingOperationLine(
                o.Sequence, o.OperationId, o.MachineId, o.OverrideDuration, o.DurationUnit, o.Notes)).ToList());
        await _routings.SaveAsync(req, ct);
        return Json(new { ok = true });
    }

    private async Task<object> BuildRoutingsBoardConfigAsync(CancellationToken ct)
    {
        var routings = await _routings.ListAsync(itemId: null, ct);

        // Master widget şablonu — admin SmartBoardConfigPanel için
        var schema = await _widgetService.GetFormSchemaByCodeAsync("ROUTING_EDIT", ct);
        var masterWidgets = SmartBoardFilterHelpers.BuildAdminFormWidgets(schema);
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget("w_active",   "Durum",     "boolean"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget("w_op_count", "Operasyon", "numeric"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget("w_item",     "Mamul",     "text"));

        var recordIds = routings.Select(r => r.Id.ToString()).ToArray();
        var batchWidgets = masterWidgets.Count > 0 && recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("ROUTING_EDIT", recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        var entities = new List<object>();
        foreach (var r in routings)
        {
            var widgets = new List<object>();

            widgets.Add(new
            {
                id = "w_active", type = "data", dataType = "text",
                label = "Durum", value = r.IsActive ? "Aktif" : "Pasif", detail = (string?)null,
                color = r.IsActive ? "emerald" : "slate"
            });

            widgets.Add(new
            {
                id = "w_op_count", type = "data", dataType = "numeric",
                label = "Operasyon", value = r.OperationCount.ToString(), detail = "adım",
                color = "indigo"
            });

            if (!string.IsNullOrWhiteSpace(r.ItemCode))
            {
                widgets.Add(new
                {
                    id = "w_item", type = "data", dataType = "text",
                    label = "Mamul",
                    value = r.ItemCode!,
                    detail = r.ItemName,
                    color = "blue"
                });
            }
            else
            {
                widgets.Add(new
                {
                    id = "w_item", type = "data", dataType = "text",
                    label = "Mamul", value = "Şablon", detail = "Item bağı yok",
                    color = "slate"
                });
            }

            // Dinamik widget'lar
            var recordId = r.Id.ToString();
            if (batchWidgets.TryGetValue(recordId, out var dtos))
            {
                foreach (var w in dtos)
                {
                    widgets.Add(new
                    {
                        id = w.WidgetId,
                        type = "data",
                        dataType = w.DataType.ToLowerInvariant(),
                        label = w.Label,
                        value = w.Value,
                        isPlainField = w.IsPlainField,
                    });
                }
            }

            var editUrl = $"/Production/RoutingEdit?id={r.Id}";
            entities.Add(new
            {
                id = r.Id,
                title = r.Name,
                subtitle = r.Code,
                description = r.Description ?? string.Empty,
                imageUrl = (string?)null,
                statusBadge = new { label = r.IsActive ? "Aktif" : "Pasif", color = r.IsActive ? "emerald" : "slate" },
                widgets,
                primaryAction = new { type = "navigate", hideButton = true, url = editUrl },
                secondaryAction = (object?)null,
                extraActions = new object?[]
                {
                    new { icon = "Edit2", color = "amber", tooltip = "Düzenle", type = "navigate", url = editUrl },
                    r.IsActive
                        ? (object)new { icon = "ToggleRight", color = "orange", tooltip = "Pasife Al", type = "api-post",
                            url = $"/Production/RoutingToggle?id={r.Id}&enabled=false" }
                        : (object)new { icon = "ToggleLeft", color = "emerald", tooltip = "Aktife Al", type = "api-post",
                            url = $"/Production/RoutingToggle?id={r.Id}&enabled=true" },
                    new { icon = "Trash2", color = "red", tooltip = "Sil", type = "api-post",
                        url = $"/Production/DeleteRouting/{r.Id}",
                        confirm = $"Bu rotayı silmek istediğinize emin misiniz? ({r.Code})" },
                },
            });
        }

        return new
        {
            boardKey = "production-routings",
            title = "Rota Tanımlamaları",
            subtitle = $"{entities.Count} rota",
            icon = "Workflow",
            iconColor = "indigo",
            refreshUrl = "/Production/Routings/BoardEntities",
            searchPlaceholder = "Hızlı ara... (kod, ad, mamul)",
            emptyText = "Henüz rota tanımlanmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Rota", icon = "Plus", variant = "primary", url = "/Production/RoutingEdit" },
            },
            masterWidgets,
            entities,
        };
    }

    [HttpGet]
    public async Task<IActionResult> RoutingEdit(int? id, CancellationToken ct)
    {
        RoutingDto? header = null;
        IReadOnlyCollection<RoutingOperationDto> operations = Array.Empty<RoutingOperationDto>();
        if (id.HasValue && id.Value > 0)
        {
            header = await _routings.GetAsync(id.Value, ct);
            if (header is null) return NotFound();
            operations = await _routings.GetOperationsAsync(id.Value, ct);
        }
        ViewBag.Operations = operations;
        return View(header);
    }

    [HttpGet]
    public async Task<IActionResult> RoutingsList(int? itemId, CancellationToken ct)
    {
        var list = await _routings.ListAsync(itemId, ct);
        return Json(list);
    }

    // ── Routing API'ları (Operasyon detayında "Rota" tab + ana Routings ekranı) ─
    [HttpGet]
    public async Task<IActionResult> RoutingsByOperation(int operationId, CancellationToken ct)
    {
        var list = await _routings.ListByOperationAsync(operationId, ct);
        return Json(list);
    }

    [HttpGet]
    public async Task<IActionResult> Routing(int id, CancellationToken ct)
    {
        var dto = await _routings.GetAsync(id, ct);
        if (dto is null) return NotFound();
        var ops = await _routings.GetOperationsAsync(id, ct);
        return Json(new { header = dto, operations = ops });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.RoutingEdit)]
    public async Task<IActionResult> SaveRouting([FromBody] SaveRoutingRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _routings.SaveAsync(req, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Routing.Save] id={Id} kaydedilemedi.", req?.Id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.RoutingEdit)]
    public async Task<IActionResult> DeleteRouting(int id, CancellationToken ct)
    {
        try
        {
            await _routings.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Routing.Delete] id={Id} silinemedi.", id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpGet("/Production/RoutingItemMaps")]
    public async Task<IActionResult> RoutingItemMaps([FromQuery] int routingId, CancellationToken ct)
    {
        var maps = await _routings.GetItemMapsAsync(routingId, ct);
        return Json(maps);
    }

    [HttpPost("/Production/AddRoutingItemMap")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.RoutingEdit)]
    public async Task<IActionResult> AddRoutingItemMap(
        [FromQuery] int routingId, [FromQuery] int itemId, [FromQuery] int? configId, CancellationToken ct)
    {
        if (routingId <= 0 || itemId <= 0)
            return Json(new { ok = false, error = "Geçersiz parametre" });
        try
        {
            var id = await _routings.AddItemMapAsync(routingId, itemId, configId, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Routing.AddItemMap] routingId={RoutingId} itemId={ItemId} eşleme eklenemedi.", routingId, itemId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost("/Production/DeleteRoutingItemMap")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.RoutingEdit)]
    public async Task<IActionResult> DeleteRoutingItemMap([FromQuery] int id, CancellationToken ct)
    {
        if (id <= 0) return Json(new { ok = false, error = "Geçersiz ID" });
        try
        {
            await _routings.DeleteItemMapAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Routing.DeleteItemMap] id={Id} eşleme silinemedi.", id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ── Operation × Machine süre eşleştirmeleri ───────────────────────────────
    // Bu üç uç hem Operasyon Düzenleme (OPERATION_EDIT) hem Rota (ROUTING_EDIT) ekranından
    // çağrılır → çoklu-kapsam: iki formdan HERHANGİ BİRİNDE grant'lı kullanıcı geçer (Seq 41).
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScopeAny(FormCodes.OperationEdit, FormCodes.RoutingEdit)]
    public async Task<IActionResult> OperationMachineTimes(int operationId, int? routingId, CancellationToken ct)
    {
        // routingId null → yalnız rota-bağımsız (ortak) satırlar; dolu → ortak + o rotaya özel satırlar.
        var list = await _machineTimes.ListByOperationAsync(operationId, routingId, ct);
        return Json(list);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScopeAny(FormCodes.OperationEdit, FormCodes.RoutingEdit)]
    public async Task<IActionResult> SaveOperationMachineTime([FromBody] SaveOperationMachineTimeRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _machineTimes.SaveAsync(req, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Operation.SaveMachineTime] operationId={OperationId} makine süresi kaydedilemedi.", req?.OperationId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScopeAny(FormCodes.OperationEdit, FormCodes.RoutingEdit)]
    public async Task<IActionResult> DeleteOperationMachineTime(int id, CancellationToken ct)
    {
        try
        {
            await _machineTimes.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Operation.DeleteMachineTime] id={Id} makine süresi silinemedi.", id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ── Üretim Tanımlamaları (Personel + Makine + Operasyon + Rota + Aktivite Sebebi sekmeli) ──
    // GET  /Production/Definitions               → Sekmeli liste view (varsayilan tab Personel)
    // GET  /Production/Personnel                 → /Production/Definitions'a 301 redirect (eski URL)
    // GET  /Production/PersonnelEdit?id=         → master-detail form
    // GET  /Production/PersonnelList?...         → JSON liste (filtreli)
    // GET  /Production/PersonnelById/{id}        → JSON tekil
    // POST /Production/SavePersonnel             → JSON (id=0 yeni, id>0 update)
    // POST /Production/DeletePersonnel/{id}      → JSON
    //
    // 2026-06-04: Action ismi Personnel → Definitions olarak değiştirildi. Sayfa
    // sadece personel değil tüm üretim tanımlamalarını (sekmeli) içerdiği için
    // URL daha anlamlı oldu. View dosyası aynı (Personnel.cshtml) — Views klasörü
    // yeniden adlandırılmadı (tüm referansları kırmamak için).
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PersonnelEdit)]
    public async Task<IActionResult> Definitions(CancellationToken ct)
    {
        var boardConfig = await BuildPersonnelBoardConfigAsync(ct);
        return View("Personnel", new PersonnelViewModel { BoardConfig = boardConfig });
    }

    [HttpGet]
    public IActionResult Personnel() => RedirectToAction(nameof(Definitions));

    private async Task<object> BuildPersonnelBoardConfigAsync(CancellationToken ct)
    {
        var people = await _personnel.ListAsync(includeInactive: true, onlyOperators: false, ct);

        // Master widget şablonu — Operations.cshtml ile aynı dinamik widget desteği
        var schema = await _widgetService.GetFormSchemaByCodeAsync("PERSONNEL_EDIT", ct);
        var masterWidgets = SmartBoardFilterHelpers.BuildAdminFormWidgets(schema);
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget("w_active",   "Durum",             "boolean"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget("w_operator", "Üretim Operatörü",  "text"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget("w_title",    "Ünvan",             "text"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget("w_dept",     "Departman",         "text"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget("w_pin",      "PIN",               "text"));
        masterWidgets.Add(SmartBoardFilterHelpers.MakeStdWidget("w_card",     "Kart No",           "text"));

        var recordIds = people.Select(p => p.Id.ToString()).ToArray();
        var batchWidgets = masterWidgets.Count > 0 && recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("PERSONNEL_EDIT", recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        // SmartBoardBuilder ile yeniden yazildi (rapor §2.5) — eski 150+ satir anonymous
        // type boilerplate fluent API ile ~55 satira indi.
        return CalibraHub.Application.SmartBoard.SmartBoard.For(people)
            .WithBoardKey("production-personnel")
            .WithTitle("Personel Tanımlamaları", subtitle: $"{people.Count} personel")
            .WithIcon("Users", "indigo")
            .WithSearchPlaceholder("Hızlı ara... (kod, ad, departman, ünvan)")
            .WithEmptyText("Henüz personel tanımlanmamış")
            .AddHeaderAction("new", "Yeni Personel", "Plus", "/Production/PersonnelEdit")
            .WithMasterWidgets(masterWidgets)
            .MapEntities(p =>
            {
                var eb = CalibraHub.Application.SmartBoard.SmartBoardEntity
                    .For(p.Id, p.FullName, subtitle: p.Title ?? p.Department ?? string.Empty)
                    .AddStatusWidget("w_active", "Durum", p.IsActive);

                if (p.IsProductionOperator)
                    eb.AddTextWidget("w_operator", "Üretim Operatörü", "Evet", color: "indigo");
                if (!string.IsNullOrWhiteSpace(p.Title))
                    eb.AddTextWidget("w_title", "Ünvan", p.Title!, color: "slate");
                if (!string.IsNullOrWhiteSpace(p.Department))
                    eb.AddTextWidget("w_dept", "Departman", p.Department!, color: "blue");
                if (!string.IsNullOrWhiteSpace(p.PinCode))
                    eb.AddTextWidget("w_pin", "PIN", "•••••", detail: "Tablet girişi", color: "amber");
                if (!string.IsNullOrWhiteSpace(p.CardNo))
                    eb.AddTextWidget("w_card", "Kart No", p.CardNo!, detail: "NFC", color: "rose");

                if (batchWidgets.TryGetValue(p.Id.ToString(), out var dtos))
                {
                    eb.AppendWidgets(dtos.Select(w => (object)new
                    {
                        id           = w.WidgetId,
                        type         = "data",
                        dataType     = w.DataType.ToLowerInvariant(),
                        label        = w.Label,
                        value        = w.Value,
                        isPlainField = w.IsPlainField,
                    }));
                }

                return eb.WithEditAndDelete(
                    editUrl:       $"/Production/PersonnelEdit?id={p.Id}",
                    deleteApiUrl:  $"/Production/DeletePersonnel/{p.Id}",
                    deleteConfirm: $"Bu personeli silmek istediğinize emin misiniz? ({p.FullName})");
            })
            .Build();
    }

    [HttpGet]
    public async Task<IActionResult> PersonnelEdit(int? id, CancellationToken ct)
    {
        PersonnelDto? dto = null;
        if (id.HasValue && id.Value > 0)
        {
            dto = await _personnel.GetAsync(id.Value, ct);
            if (dto is null) return NotFound();
        }

        var locs = await _logisticsConfig.GetLocationsAsync(ct);
        var locParentIds = locs.Where(l => l.ParentId.HasValue).Select(l => l.ParentId!.Value).ToHashSet();
        ViewData["PersonnelLocationList"] = locs
            .Where(l => l.IsActive && !locParentIds.Contains(l.Id))
            .OrderBy(l => l.LocationName ?? l.LocationCode)
            .Select(l => new { l.Id, Name = l.LocationName ?? l.LocationCode })
            .ToList();

        return View(dto);
    }

    [HttpGet]
    public async Task<IActionResult> PersonnelList(bool includeInactive, bool onlyOperators, CancellationToken ct)
    {
        var list = await _personnel.ListAsync(includeInactive, onlyOperators, ct);
        return Json(list);
    }

    [HttpGet]
    public async Task<IActionResult> PersonnelById(int id, CancellationToken ct)
    {
        var dto = await _personnel.GetAsync(id, ct);
        if (dto is null) return NotFound();
        return Json(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PersonnelEdit)]
    public async Task<IActionResult> SavePersonnel([FromBody] SavePersonnelRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _personnel.SaveAsync(req, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Personnel.Save] id={Id} kaydedilemedi.", req?.Id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PersonnelEdit)]
    public async Task<IActionResult> DeletePersonnel(int id, CancellationToken ct)
    {
        try
        {
            await _personnel.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Personnel.Delete] id={Id} silinemedi.", id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ── Faz 3a-5: WorkOrderOperation API'ları (Rota tab) ──────────────────────
    // GET  /Production/WorkOrderOperations?workOrderId   → JSON liste (sıralı)
    // GET  /Production/WorkOrderOperation/{id}           → JSON tekil
    // POST /Production/SaveWorkOrderOperation            → JSON (id=0 yeni, id>0 update)
    // POST /Production/DeleteWorkOrderOperation/{id}     → JSON
    // POST /Production/ExplodeFromRouting                → JSON (Routing → WorkOrderOperation kopya)
    [HttpGet]
    public async Task<IActionResult> WorkOrderOperations(int workOrderId, CancellationToken ct)
    {
        var list = await _workOrderOperations.GetByWorkOrderAsync(workOrderId, ct);
        return Json(list);
    }

    [HttpGet]
    public async Task<IActionResult> WorkOrderOperation(int id, CancellationToken ct)
    {
        var dto = await _workOrderOperations.GetAsync(id, ct);
        if (dto is null) return NotFound();
        return Json(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> SaveWorkOrderOperation([FromBody] SaveWorkOrderOperationRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _workOrderOperations.SaveAsync(req, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.SaveOperation] id={Id} workOrderId={WorkOrderId} operasyon kaydedilemedi.", req?.Id, req?.WorkOrderId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> DeleteWorkOrderOperation(int id, CancellationToken ct)
    {
        try
        {
            await _workOrderOperations.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.DeleteOperation] id={Id} operasyon silinemedi.", id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record ExplodeFromRoutingRequest(int WorkOrderId, int RoutingId);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> ExplodeFromRouting([FromBody] ExplodeFromRoutingRequest req, CancellationToken ct)
    {
        try
        {
            if (req.WorkOrderId <= 0 || req.RoutingId <= 0)
                return Json(new { ok = false, error = "WorkOrderId ve RoutingId zorunlu." });
            await _workOrderOperations.ExplodeFromRoutingAsync(req.WorkOrderId, req.RoutingId, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.ExplodeFromRouting] workOrderId={WorkOrderId} routingId={RoutingId} rota patlatılamadı.", req?.WorkOrderId, req?.RoutingId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ─── Faz 2: BOM Patlatma (WorkOrderComponent) ────────────────────────────────
    // POST /Production/ExplodeBom/{workOrderId}                → reçeteyi patlat (idempotent)
    // GET  /Production/WorkOrderComponents?workOrderId=        → patlatılmış bileşen listesi
    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> ExplodeBom(int workOrderId, CancellationToken ct)
    {
        try
        {
            var result = await _service.ExplodeBomAsync(workOrderId, ct);
            return Json(new { ok = true, result });
        }
        catch (InvalidOperationException iex)
        {
            // Guard mesajları (reçete yok / sarf başladı / üretim kilidi) KULLANICIYA GÖRE
            // yazılmıştır — jenerik mesajla gizlenirse kullanıcı neden patlatamadığını
            // anlayamaz (2026-08-06; iç detay sızdırmaz, sessiz-kırık kural #2 ile uyumlu).
            return Json(new { ok = false, error = iex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.ExplodeBom] workOrderId={WorkOrderId} reçete patlatılamadı.", workOrderId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>
    /// İş emri "Üretim Hareketleri" sekmesinin verisi (2026-08-24 kullanıcı isteği:
    /// "iş emri içerisinden üretim sonu kayıtlarını ve üretim akış kayıtlarını da görebilmeli
    /// ve ilgili kayda gidebilmeliyiz").
    ///
    /// İki farklı kayıt türü tek çağrıda döner:
    ///   movements  — mamul girişi + bileşen sarfı (stok etkisi olan satırlar)
    ///   activities — operasyon bazlı üretim akışı (başlat/üret/durma kayıtları)
    ///
    /// Aktivite geçmişi operasyon başına saklanır; burada iş emrinin TÜM operasyonları için
    /// toplanıp tek zaman çizelgesine indirilir (en yeni önce). Operasyon adı satıra
    /// eklenir ki kullanıcı hangi adımın kaydı olduğunu görebilsin.
    /// </summary>
    /// <summary>
    /// Üretim fişi görüntüleme (2026-08-24, Faz 2) — salt okunur. İş emri ekranındaki
    /// "Fişe Git" bağlantısının hedefi. Fiş, iş emrinin bir üretim işlemini (sarf ya da
    /// mamul girişi) temsil eden ayrı bir belgedir.
    /// </summary>
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> ProductionVoucher(int id, CancellationToken ct)
    {
        if (id <= 0) return NotFound();
        var v = await _workOrderRepo.GetVoucherAsync(id, ct);
        if (string.IsNullOrWhiteSpace(v.VoucherNo)) return NotFound();
        ViewData["Title"] = $"Üretim Fişi {v.VoucherNo}";
        return View(v);
    }

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> WorkOrderMovements(int workOrderId, CancellationToken ct)
    {
        if (workOrderId <= 0) return Json(new { ok = false, error = "İş emri belirtilmedi." });
        try
        {
            var movements = await _workOrderRepo.GetMovementsAsync(workOrderId, ct);
            var operations = await _workOrderOperations.GetByWorkOrderAsync(workOrderId, ct);

            var activities = new List<ActivityRow>();
            foreach (var op in operations)
            {
                // OPERASYONUN KENDİ akış kaydı. Önceki sürüm yalnız saha aktivite loguna
                // (WorkOrderOperationActivity) bakıyordu; o log YALNIZCA ShopFloor "Durum
                // Değiştir" menüsü kullanıldığında oluşuyor. Operasyonu başlat/üret/tamamla
                // akışı ise operasyon satırının kendisine yazılıyor — bu yüzden üretim yapılmış
                // iş emirlerinde bile bölüm "0 kayıt" görünüyordu (2026-08-24).
                if (op.StartedAt.HasValue || op.ProducedQuantity > 0 || op.CompletedAt.HasValue)
                {
                    var durationSec = (op.StartedAt.HasValue && op.CompletedAt.HasValue)
                        ? (int)Math.Max(0, (op.CompletedAt.Value - op.StartedAt.Value).TotalSeconds)
                        : (int?)null;
                    activities.Add(new ActivityRow
                    {
                        id = -op.Id,                        // negatif: aktivite değil, operasyonun kendisi
                        operationId = op.Id,
                        sequence = op.Sequence,
                        operationName = op.OperationName ?? op.Name ?? $"Sıra {op.Sequence}",
                        activityType = op.CompletedAt.HasValue ? "Operasyon tamamlandı"
                                     : op.StartedAt.HasValue ? "Operasyon devam ediyor"
                                     : "Üretim girişi",
                        reason = null,
                        personnel = op.CompletedByPersonnelName ?? op.StartedByPersonnelName,
                        startedAt = op.StartedAt ?? DateTime.UtcNow,
                        endedAt = op.CompletedAt,
                        durationSeconds = durationSec,
                        quantity = op.ProducedQuantity,
                        scrapQuantity = op.ScrapQuantity > 0 ? op.ScrapQuantity : null,
                        notes = op.Notes,
                    });
                }

                var history = await _activities.GetHistoryAsync(op.Id, ct);
                foreach (var h in history)
                {
                    activities.Add(new ActivityRow
                    {
                        id = h.Id,
                        operationId = op.Id,
                        sequence = op.Sequence,
                        operationName = op.OperationName ?? op.Name ?? $"Sıra {op.Sequence}",
                        activityType = h.ActivityTypeLabel,
                        reason = h.ActivityReasonName,
                        personnel = h.PersonnelName,
                        startedAt = h.StartedAt,
                        endedAt = h.EndedAt,
                        durationSeconds = h.DurationSeconds,
                        quantity = h.Quantity,
                        scrapQuantity = h.ScrapQuantity,
                        notes = h.Notes,
                    });
                }
            }

            return Json(new
            {
                ok = true,
                movements,
                activities = activities.OrderByDescending(a => a.startedAt).ToArray(),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.Movements] workOrderId={WorkOrderId} üretim hareketleri okunamadı.", workOrderId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>Üretim akışı satırı — arayüze camelCase alanlarla düz gider.</summary>
    private sealed class ActivityRow
    {
        public int id { get; set; }
        public int operationId { get; set; }
        public int sequence { get; set; }
        public string? operationName { get; set; }
        public string? activityType { get; set; }
        public string? reason { get; set; }
        public string? personnel { get; set; }
        public DateTime startedAt { get; set; }
        public DateTime? endedAt { get; set; }
        public int? durationSeconds { get; set; }
        public decimal? quantity { get; set; }
        public decimal? scrapQuantity { get; set; }
        public string? notes { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> WorkOrderComponents(int workOrderId, CancellationToken ct)
    {
        var list = await _service.GetComponentsAsync(workOrderId, ct);
        return Json(list);
    }

    public sealed record UpdateComponentLocationRequest(int ComponentId, int? LocationId);

    /// <summary>
    /// Bir bileşenin planlı sarf lokasyonunu (FromLocationId) günceller — ExplodeBom'un
    /// item-default önerisini kullanıcı burada override eder (2026-07-31).
    /// </summary>
    [HttpPost("Production/WorkOrder/UpdateComponentLocationJson")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> UpdateComponentLocationJson([FromBody] UpdateComponentLocationRequest req, CancellationToken ct)
    {
        if (req is null || req.ComponentId <= 0)
            return Json(new { ok = false, error = "Bileşen kaydı zorunlu." });
        try
        {
            var (ok, error) = await _service.UpdateComponentLocationAsync(req.ComponentId, req.LocationId, CurrentUserId(), ct);
            return Json(new { ok, error });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.UpdateComponentLocation] componentId={ComponentId} lokasyon güncellenemedi.", req?.ComponentId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ─── Reçete versiyonlama + iş emri bileşen özelleştirme (2026-08-06) ────────
    // GET  /Production/WorkOrder/BomOptions?workOrderId=   → baz + versiyonlar + seçim
    // POST /Production/WorkOrder/SetBomJson                → reçete seçimini değiştir
    // POST /Production/WorkOrder/AddComponentJson          → bileşen ekle
    // POST /Production/WorkOrder/UpdateComponentJson       → miktar/fire/not güncelle
    // POST /Production/WorkOrder/DeleteComponentJson       → bileşen sil
    // Guard'lar service'te: üretim başladıysa ALLOW_STARTED_WO_RECIPE_EDIT parametresi,
    // sarflı satırda silme/azaltma yasağı (parametreden bağımsız).

    [HttpGet("Production/WorkOrder/BomOptions")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> WorkOrderBomOptions(int workOrderId, CancellationToken ct)
    {
        try
        {
            var (selectedBomId, options) = await _service.GetBomOptionsAsync(workOrderId, ct);
            return Json(new
            {
                ok = true,
                selectedBomId,
                options = options.Select(o => new
                {
                    id = o.Id,
                    versionCode = o.VersionCode,           // null = baz
                    label = o.VersionCode ?? "Baz Reçete",
                    lineCount = o.LineCount,
                    description = o.Description,
                }),
            });
        }
        catch (InvalidOperationException iex) { return Json(new { ok = false, error = iex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.BomOptions] workOrderId={WorkOrderId} okunamadı.", workOrderId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record SetWorkOrderBomRequest(int WorkOrderId, int? BomId);

    [HttpPost("Production/WorkOrder/SetBomJson")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> SetWorkOrderBomJson([FromBody] SetWorkOrderBomRequest req, CancellationToken ct)
    {
        if (req is null || req.WorkOrderId <= 0)
            return Json(new { ok = false, error = "İş emri zorunlu." });
        try
        {
            await _service.SetBomAsync(req.WorkOrderId, req.BomId, CurrentUserId(), ct);
            return Json(new { ok = true });
        }
        catch (InvalidOperationException iex) { return Json(new { ok = false, error = iex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.SetBom] workOrderId={WorkOrderId} reçete seçimi kaydedilemedi.", req?.WorkOrderId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record AddWorkOrderComponentRequest(
        int WorkOrderId, int ItemId, int? ConfigId, decimal Quantity, decimal ScrapRate, string? Notes);

    [HttpPost("Production/WorkOrder/AddComponentJson")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> AddWorkOrderComponentJson([FromBody] AddWorkOrderComponentRequest req, CancellationToken ct)
    {
        if (req is null || req.WorkOrderId <= 0)
            return Json(new { ok = false, error = "İş emri zorunlu." });
        try
        {
            var id = await _service.AddComponentAsync(
                req.WorkOrderId, req.ItemId, req.ConfigId, req.Quantity, req.ScrapRate, req.Notes, ct);
            return Json(new { ok = true, id });
        }
        catch (InvalidOperationException iex) { return Json(new { ok = false, error = iex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.AddComponent] workOrderId={WorkOrderId} bileşen eklenemedi.", req?.WorkOrderId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record UpdateWorkOrderComponentRequest(int ComponentId, decimal Quantity, decimal ScrapRate, string? Notes);

    [HttpPost("Production/WorkOrder/UpdateComponentJson")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> UpdateWorkOrderComponentJson([FromBody] UpdateWorkOrderComponentRequest req, CancellationToken ct)
    {
        if (req is null || req.ComponentId <= 0)
            return Json(new { ok = false, error = "Bileşen kaydı zorunlu." });
        try
        {
            await _service.UpdateComponentAsync(req.ComponentId, req.Quantity, req.ScrapRate, req.Notes, ct);
            return Json(new { ok = true });
        }
        catch (InvalidOperationException iex) { return Json(new { ok = false, error = iex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.UpdateComponent] componentId={ComponentId} güncellenemedi.", req?.ComponentId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record DeleteWorkOrderComponentRequest(int ComponentId);

    [HttpPost("Production/WorkOrder/DeleteComponentJson")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.WorkOrderEdit)]
    public async Task<IActionResult> DeleteWorkOrderComponentJson([FromBody] DeleteWorkOrderComponentRequest req, CancellationToken ct)
    {
        if (req is null || req.ComponentId <= 0)
            return Json(new { ok = false, error = "Bileşen kaydı zorunlu." });
        try
        {
            await _service.DeleteComponentAsync(req.ComponentId, ct);
            return Json(new { ok = true });
        }
        catch (InvalidOperationException iex) { return Json(new { ok = false, error = iex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WorkOrder.DeleteComponent] componentId={ComponentId} silinemedi.", req?.ComponentId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ─── Makine Planlama (Üretim Çizelgeleme) — Faz 1 Manuel (2026-08-04) ────────
    // API sözleşmesi KİLİTLİ (bkz. plan glittery-finding-comet.md) — frontend
    // machineScheduleService.js bu üç endpoint'e göre yazıldı, alan adı/tipini değiştirme.
    // GET  /Production/MachineSchedule                           → Gantt canvas view (React mount)
    // GET  /Production/MachineScheduleData?from=&to=             → makineler + bloklar + planlanacak kuyruğu
    // POST /Production/SaveScheduleBlock                         → oluştur/taşı (çakışma uyarı olarak döner)
    // POST /Production/DeleteScheduleBlock                       → soft-delete
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineSchedule)]
    public IActionResult MachineSchedule() => View();

    [HttpGet("Production/MachineScheduleData")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineSchedule)]
    public async Task<IActionResult> MachineScheduleData(DateTime from, DateTime to, int? scenarioId, CancellationToken ct)
    {
        try
        {
            // Frontend UTC "...Z" ISO gönderir; MVC query binder bunu yerel saate çevirip
            // Kind=Local (doğru instant, yanlış etiket) döner. SpecifyKind saat rakamını
            // değiştirmeden etiketi UTC yaparsa pencere sunucu offset'i kadar kayar ve kenardaki
            // bloklar sessizce düşer. ToUniversalTime doğru instant'ı verir (Utc ise no-op).
            var fromUtc = from.ToUniversalTime();
            var toUtc = to.ToUniversalTime();
            // Vardiya Senaryoları (2026-08-05) — scenarioId null ise varsayılan senaryo türetilir.
            var data = await _machineSchedule.GetScheduleDataAsync(fromUtc, toUtc, scenarioId, ct);
            return Json(new {
                ok = true, machines = data.Machines, blocks = data.Blocks, unplanned = data.Unplanned,
                // Faz 2 (2026-08-05) — Gantt gölgeleme (müsaitlik dışı + tatil) ham verisi.
                workWindows = data.WorkWindows, holidays = data.Holidays,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MachineSchedule.Data] from={From} to={To} veri alınamadı.", from, to);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineSchedule)]
    public async Task<IActionResult> SaveScheduleBlock([FromBody] SaveScheduleBlockRequest req, CancellationToken ct)
    {
        if (req is null || req.MachineId <= 0)
            return Json(new { ok = false, error = "Makine zorunlu." });
        if (req.EndUtc <= req.StartUtc)
            return Json(new { ok = false, error = "Bitiş zamanı başlangıçtan sonra olmalı." });
        try
        {
            var isNew = req.Id <= 0;
            var result = await _machineSchedule.SaveBlockAsync(req, CurrentUserId(), ct);
            // Audit: yalnız yeni blok oluşturmayı logla. Taşıma/yeniden-boyutlandırma (req.Id>0)
            // Gantt'ta sık update ürettiğinden bilinçli olarak loglanmaz (gürültü önleme).
            if (isNew)
            {
                _audit.LogInsert("MachineScheduleBlock", result.Id,
                    $"{BlockTypeLabel(req.BlockType)} bloğu — Makine #{req.MachineId}",
                    detail: $"{req.StartUtc:yyyy-MM-dd HH:mm} → {req.EndUtc:yyyy-MM-dd HH:mm} (UTC)");
            }
            return Json(new { ok = result.Ok, id = result.Id, conflicts = result.Conflicts });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MachineSchedule.SaveBlock] id={Id} machineId={MachineId} kaydedilemedi.", req.Id, req.MachineId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record DeleteScheduleBlockRequest(int Id);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineSchedule)]
    public async Task<IActionResult> DeleteScheduleBlock([FromBody] DeleteScheduleBlockRequest req, CancellationToken ct)
    {
        if (req is null || req.Id <= 0)
            return Json(new { ok = false, error = "Kayıt belirtilmedi." });
        try
        {
            await _machineSchedule.DeleteBlockAsync(req.Id, CurrentUserId(), ct);
            _audit.LogDelete("MachineScheduleBlock", req.Id, $"Planlama bloğu #{req.Id}");
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MachineSchedule.DeleteBlock] id={Id} silinemedi.", req.Id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ─── Blok Kilitle + Yeniden Çizelgele (2026-08-22) — kilitleme ───────────────
    // API sözleşmesi KİLİTLİ — frontend machineScheduleService.js bu iki endpoint'e göre yazılır.
    // Yalnız Status alanı değişir (start/end/tip dokunulmaz). Setup child (ParentBlockId dolu)
    // bloklar elle kilitlenemez — repo katmanında [ParentBlockId] IS NULL ile hariç tutulur.
    // POST /Production/SetBlockStatus                            → tekil durum değişimi
    // POST /Production/BulkSetBlockStatus                        → toplu durum değişimi
    public sealed record SetBlockStatusRequest(int Id, byte Status);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineSchedule)]
    public async Task<IActionResult> SetBlockStatus([FromBody] SetBlockStatusRequest req, CancellationToken ct)
    {
        if (req is null || req.Id <= 0)
            return Json(new { ok = false, error = "Kayıt belirtilmedi." });
        if (req.Status is < 1 or > 3)
            return Json(new { ok = false, error = "Geçersiz durum." });
        try
        {
            var ok = await _machineSchedule.SetBlockStatusAsync(req.Id, req.Status, CurrentUserId(), ct);
            if (!ok)
                return Json(new { ok = false, error = "Kayıt bulunamadı veya hazırlık (setup) alt bloğunun durumu ayrı değiştirilemez." });
            _audit.LogEvent("MachineScheduleBlock.SetStatus", detail: $"Blok #{req.Id} → {BlockStatusLabel(req.Status)}.");
            return Json(new { ok = true, id = req.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MachineSchedule.SetBlockStatus] id={Id} status={Status} güncellenemedi.", req.Id, req.Status);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record BulkSetBlockStatusRequest(IReadOnlyList<int> Ids, byte Status);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineSchedule)]
    public async Task<IActionResult> BulkSetBlockStatus([FromBody] BulkSetBlockStatusRequest req, CancellationToken ct)
    {
        if (req is null || req.Ids is null || req.Ids.Count == 0)
            return Json(new { ok = false, error = "En az bir blok seçilmeli." });
        if (req.Status is < 1 or > 3)
            return Json(new { ok = false, error = "Geçersiz durum." });
        try
        {
            var count = await _machineSchedule.BulkSetBlockStatusAsync(req.Ids, req.Status, CurrentUserId(), ct);
            _audit.LogEvent("MachineScheduleBlock.BulkSetStatus",
                detail: $"{count} blok → {BlockStatusLabel(req.Status)} ({req.Ids.Count} seçildi).");
            return Json(new { ok = true, count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MachineSchedule.BulkSetBlockStatus] count={Count} status={Status} güncellenemedi.", req.Ids?.Count, req.Status);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ─── Makine Çalışma Takvimi (haftalık müsaitlik + resmi tatil) — Faz 2 (2026-08-05) ───────
    // GET  /Production/MachineCalendar                            → admin ekran (React mount)
    // GET  /Production/MachineCalendarData                        → makineler + haftalık pencereler + tatiller
    // POST /Production/SaveMachineWorkWindow                      → oluştur/güncelle
    // POST /Production/DeleteMachineWorkWindow                    → soft-delete
    // POST /Production/SaveHoliday                                → oluştur/güncelle
    // POST /Production/DeleteHoliday                              → soft-delete
    // Sözleşme KİLİTLİ: dayOfWeek 0=Pazar..6=Cumartesi (JS Date.getDay()); startMinute/endMinute
    // yerel gün-ortası dakikası 0..1440 (duvar saati — UTC DEĞİL, haftalık tekrar); tatil
    // date="yyyy-MM-dd" (yerel DATE).
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineCalendar)]
    public IActionResult MachineCalendar() => View();

    [HttpGet("Production/MachineCalendarData")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineCalendar)]
    public async Task<IActionResult> MachineCalendarData(CancellationToken ct)
    {
        try
        {
            var machines = await _machineCalendar.ListActiveMachinesAsync(ct);
            var windows = await _machineCalendar.ListWorkWindowsAsync(ct);
            var holidays = await _machineCalendar.ListHolidaysAsync(ct);
            // Vardiya Senaryoları (2026-08-05) — matris editörü için senaryo listesi + vardiya paleti.
            // machines/windows/holidays geri-uyum için AYNEN kalır (legacy alan adları).
            var scenarios = await _machineCalendar.ListScenariosAsync(ct);
            var shifts = (await _shifts.ListAsync(includeInactive: false, ct))
                .Select(s => new { id = s.Id, name = s.Name, code = s.Code, startTime = s.StartTime, endTime = s.EndTime, colorHex = s.ColorHex })
                .ToArray();
            return Json(new { ok = true, machines, windows, holidays, scenarios, shifts });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MachineCalendar.Data] veri alınamadı.");
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineCalendar)]
    public async Task<IActionResult> SaveMachineWorkWindow([FromBody] SaveMachineWorkWindowRequest req, CancellationToken ct)
    {
        if (req is null || req.MachineId <= 0)
            return Json(new { ok = false, error = "Makine zorunlu." });
        if (req.DayOfWeek > 6)
            return Json(new { ok = false, error = "Geçersiz gün." });
        if (req.StartMinute < 0 || req.EndMinute > 1440 || req.EndMinute <= req.StartMinute)
            return Json(new { ok = false, error = "Geçersiz saat aralığı." });
        try
        {
            var isNew = req.Id <= 0;
            var oldSnapshot = isNew ? null : await _machineCalendar.GetWorkWindowAsync(req.Id, ct);
            var id = await _machineCalendar.SaveWorkWindowAsync(req, CurrentUserId(), ct);
            if (isNew)
            {
                _audit.LogInsert("MachineWorkWindow", id, $"Çalışma Penceresi — Makine #{req.MachineId}",
                    detail: $"{DayOfWeekLabel(req.DayOfWeek)} {MinuteLabel(req.StartMinute)}-{MinuteLabel(req.EndMinute)}");
            }
            else if (oldSnapshot is not null)
            {
                _audit.LogUpdate("MachineWorkWindow", id, $"Çalışma Penceresi — Makine #{req.MachineId}", oldSnapshot, req);
            }
            return Json(new { ok = true, id });
        }
        catch (ArgumentException ex) { return Json(new { ok = false, error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MachineCalendar.SaveWorkWindow] id={Id} machineId={MachineId} kaydedilemedi.", req.Id, req.MachineId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record DeleteMachineWorkWindowRequest(int Id);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineCalendar)]
    public async Task<IActionResult> DeleteMachineWorkWindow([FromBody] DeleteMachineWorkWindowRequest req, CancellationToken ct)
    {
        if (req is null || req.Id <= 0)
            return Json(new { ok = false, error = "Kayıt belirtilmedi." });
        try
        {
            await _machineCalendar.DeleteWorkWindowAsync(req.Id, CurrentUserId(), ct);
            _audit.LogDelete("MachineWorkWindow", req.Id, $"Çalışma Penceresi #{req.Id}");
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MachineCalendar.DeleteWorkWindow] id={Id} silinemedi.", req.Id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineCalendar)]
    public async Task<IActionResult> SaveHoliday([FromBody] SaveHolidayRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Date))
            return Json(new { ok = false, error = "Tarih zorunlu." });
        if (!DateTime.TryParseExact(req.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return Json(new { ok = false, error = "Geçersiz tarih formatı." });
        try
        {
            var isNew = req.Id <= 0;
            var oldSnapshot = isNew ? null : await _machineCalendar.GetHolidayAsync(req.Id, ct);
            var id = await _machineCalendar.SaveHolidayAsync(req, CurrentUserId(), ct);
            if (isNew)
                _audit.LogInsert("CompanyHoliday", id, req.Name ?? req.Date, detail: req.Date);
            else if (oldSnapshot is not null)
                _audit.LogUpdate("CompanyHoliday", id, req.Name ?? req.Date, oldSnapshot, req);
            return Json(new { ok = true, id });
        }
        catch (ArgumentException ex) { return Json(new { ok = false, error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MachineCalendar.SaveHoliday] id={Id} kaydedilemedi.", req.Id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record DeleteHolidayRequest(int Id);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineCalendar)]
    public async Task<IActionResult> DeleteHoliday([FromBody] DeleteHolidayRequest req, CancellationToken ct)
    {
        if (req is null || req.Id <= 0)
            return Json(new { ok = false, error = "Kayıt belirtilmedi." });
        try
        {
            await _machineCalendar.DeleteHolidayAsync(req.Id, CurrentUserId(), ct);
            _audit.LogDelete("CompanyHoliday", req.Id, $"Tatil #{req.Id}");
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MachineCalendar.DeleteHoliday] id={Id} silinemedi.", req.Id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ─── Vardiya Senaryoları (2026-08-05) — Makine Çalışma Takvimi'nin senaryo-tabanlı yükseltmesi ───
    // GET  /Production/ShiftScenariosList                        → JSON senaryo listesi
    // POST /Production/SaveShiftScenario                         → oluştur/güncelle
    // POST /Production/DeleteShiftScenario                       → soft-delete (varsayılan silinemez)
    // GET  /Production/ScenarioMachineShiftsList?scenarioId=     → JSON senaryonun makine×vardiya×gün ataması
    // POST /Production/SaveScenarioMachineShift                  → oluştur/güncelle
    // POST /Production/DeleteScenarioMachineShift                → soft-delete
    [HttpGet("Production/ShiftScenariosList")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineCalendar)]
    public async Task<IActionResult> ShiftScenariosList(CancellationToken ct)
    {
        try
        {
            var items = await _machineCalendar.ListScenariosAsync(ct);
            return Json(new { ok = true, items });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShiftScenario.List] senaryo listesi alınamadı.");
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineCalendar)]
    public async Task<IActionResult> SaveShiftScenario([FromBody] SaveShiftScenarioRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Name))
            return Json(new { ok = false, error = "Senaryo adı zorunludur." });
        try
        {
            var isNew = req.Id <= 0;
            var oldSnapshot = isNew ? null : await _machineCalendar.GetScenarioAsync(req.Id, ct);
            var id = await _machineCalendar.SaveScenarioAsync(req, CurrentUserId(), ct);
            if (isNew)
                _audit.LogInsert("ShiftScenario", id, req.Name, snapshot: req);
            else if (oldSnapshot is not null)
                _audit.LogUpdate("ShiftScenario", id, req.Name, oldSnapshot, req);
            return Json(new { ok = true, id });
        }
        catch (ArgumentException ex) { return Json(new { ok = false, error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShiftScenario.Save] id={Id} senaryo kaydedilemedi.", req.Id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record DeleteShiftScenarioRequest(int Id);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineCalendar)]
    public async Task<IActionResult> DeleteShiftScenario([FromBody] DeleteShiftScenarioRequest req, CancellationToken ct)
    {
        if (req is null || req.Id <= 0)
            return Json(new { ok = false, error = "Kayıt belirtilmedi." });
        try
        {
            await _machineCalendar.DeleteScenarioAsync(req.Id, CurrentUserId(), ct);
            _audit.LogDelete("ShiftScenario", req.Id, $"Senaryo #{req.Id}");
            return Json(new { ok = true });
        }
        catch (ArgumentException ex) { return Json(new { ok = false, error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShiftScenario.Delete] id={Id} silinemedi.", req.Id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpGet("Production/ScenarioMachineShiftsList")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineCalendar)]
    public async Task<IActionResult> ScenarioMachineShiftsList(int scenarioId, CancellationToken ct)
    {
        if (scenarioId <= 0)
            return Json(new { ok = false, error = "Senaryo belirtilmedi." });
        try
        {
            var items = await _machineCalendar.ListScenarioMachineShiftsAsync(scenarioId, ct);
            return Json(new { ok = true, items });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ScenarioMachineShift.List] scenarioId={ScenarioId} atama listesi alınamadı.", scenarioId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineCalendar)]
    public async Task<IActionResult> SaveScenarioMachineShift([FromBody] SaveScenarioMachineShiftRequest req, CancellationToken ct)
    {
        if (req is null || req.ScenarioId <= 0 || req.MachineId <= 0 || req.ShiftId <= 0)
            return Json(new { ok = false, error = "Senaryo, makine ve vardiya zorunlu." });
        if (req.DaysMask > 127)
            return Json(new { ok = false, error = "Geçersiz gün maskesi." });
        try
        {
            var isNew = req.Id <= 0;
            // Update'te eski kaydı mutasyondan ÖNCE çek (CLAUDE.md audit kuralı) — tekil-get metodu
            // yok, mevcut liste zaten senaryo başına küçük (matris grid), id'ye göre filtrelenir.
            ScenarioMachineShiftDto? oldSnapshot = null;
            if (!isNew)
            {
                var existing = await _machineCalendar.ListScenarioMachineShiftsAsync(req.ScenarioId, ct);
                oldSnapshot = existing.FirstOrDefault(x => x.Id == req.Id);
            }
            var id = await _machineCalendar.SaveScenarioMachineShiftAsync(req, CurrentUserId(), ct);
            var title = $"Senaryo #{req.ScenarioId} — Makine #{req.MachineId} × Vardiya #{req.ShiftId}";
            if (isNew)
                _audit.LogInsert("ScenarioMachineShift", id, title, snapshot: req);
            else if (oldSnapshot is not null)
                _audit.LogUpdate("ScenarioMachineShift", id, title, oldSnapshot, req);
            return Json(new { ok = true, id });
        }
        catch (ArgumentException ex) { return Json(new { ok = false, error = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ScenarioMachineShift.Save] id={Id} scenarioId={ScenarioId} kaydedilemedi.", req.Id, req.ScenarioId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record DeleteScenarioMachineShiftRequest(int Id);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineCalendar)]
    public async Task<IActionResult> DeleteScenarioMachineShift([FromBody] DeleteScenarioMachineShiftRequest req, CancellationToken ct)
    {
        if (req is null || req.Id <= 0)
            return Json(new { ok = false, error = "Kayıt belirtilmedi." });
        try
        {
            await _machineCalendar.DeleteScenarioMachineShiftAsync(req.Id, CurrentUserId(), ct);
            _audit.LogDelete("ScenarioMachineShift", req.Id, $"Atama #{req.Id}");
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ScenarioMachineShift.Delete] id={Id} silinemedi.", req.Id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ─── Makine Planlama Faz 3 — Otomatik Çizelgeleme Önerisi (forward, sonlu-kapasite) (2026-08-05) ───
    // API sözleşmesi KİLİTLİ — frontend "Otomatik Yerleştir" akışı + machineScheduleService.js bu üç
    // endpoint'e göre yazılır, alan adı/tipini değiştirme. GET aday listesi döner, POST Preview yalnız
    // hesaplar (persist ETMEZ), POST Apply AYNI girdiden yeniden hesaplayıp persist eder (client'ın
    // gönderdiği blok koordinatlarına GÜVENMEZ). Tüm zaman UTC "…Z".
    // GET  /Production/AutoScheduleCandidates             → aday iş emirleri (Priority↓/PlannedEndDate↑)
    // POST /Production/AutoSchedulePreview                → önizleme (persist yok)
    // POST /Production/AutoScheduleApply                  → uygula (Status=Planned bloklar yazılır)
    [HttpGet("Production/AutoScheduleCandidates")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineSchedule)]
    public async Task<IActionResult> AutoScheduleCandidates(CancellationToken ct)
    {
        try
        {
            var workOrders = await _autoSchedule.GetCandidatesAsync(ct);
            return Json(new { ok = true, workOrders });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoSchedule.Candidates] aday liste alınamadı.");
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineSchedule)]
    public async Task<IActionResult> AutoSchedulePreview([FromBody] AutoScheduleRequest req, CancellationToken ct)
    {
        if (req is null || req.IncludedWorkOrderIds is null || req.IncludedWorkOrderIds.Count == 0)
            return Json(new { ok = false, error = "Çizelgelenecek en az bir iş emri seçilmeli." });
        try
        {
            // Frontend UTC "...Z" ISO gönderir; JSON body binder normalde Kind=Utc üretir ama
            // ToUniversalTime() no-op olarak güvenli bir ek katman (bkz. MachineScheduleData'daki
            // aynı savunma — query-string binder'daki Kind kayması riskine karşı).
            var fromUtc = req.FromUtc.ToUniversalTime();
            var result = await _autoSchedule.PreviewAsync(req.IncludedWorkOrderIds, fromUtc, req.ScenarioId, ct);
            return Json(new { ok = true, proposals = result.Proposals, unplaceable = result.Unplaceable });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoSchedule.Preview] wo sayısı={Count} önizleme hesaplanamadı.", req.IncludedWorkOrderIds?.Count);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineSchedule)]
    public async Task<IActionResult> AutoScheduleApply([FromBody] AutoScheduleRequest req, CancellationToken ct)
    {
        if (req is null || req.IncludedWorkOrderIds is null || req.IncludedWorkOrderIds.Count == 0)
            return Json(new { ok = false, error = "Çizelgelenecek en az bir iş emri seçilmeli." });
        try
        {
            var fromUtc = req.FromUtc.ToUniversalTime();
            var result = await _autoSchedule.ApplyAsync(req.IncludedWorkOrderIds, fromUtc, req.ScenarioId, CurrentUserId(), ct);
            // Toplu özet — her blok tek tek loglanmaz (gürültü önleme, bkz. CLAUDE.md audit kuralı).
            _audit.LogEvent("MachineAutoSchedule.Apply",
                detail: $"{result.CreatedCount} blok oluşturuldu, {result.UnplaceableCount} operasyon yerleştirilemedi " +
                        $"({req.IncludedWorkOrderIds.Count} iş emri, fromUtc={fromUtc:yyyy-MM-dd HH:mm} UTC).");
            return Json(new { ok = true, createdCount = result.CreatedCount, unplaceableCount = result.UnplaceableCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoSchedule.Apply] wo sayısı={Count} uygulanamadı.", req.IncludedWorkOrderIds?.Count);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ─── Blok Kilitle + Yeniden Çizelgele (2026-08-22) — yeniden çizelgele ────────
    // API sözleşmesi KİLİTLİ — frontend machineScheduleService.js bu iki endpoint'e göre yazılır.
    // Kapsam: Kilitli(2)/Onaylı(3) SABİT, diğer TÜM Planlı(1) bloklar serbest bırakılır; TÜM açık
    // iş emirleri (opt-out listesi yok). Preview PERSIST ETMEZ; Apply AYNI girdiden yeniden hesaplar
    // (client blok koordinatına GÜVENMEZ), TEK transaction'da serbest bırakır + yeniden yazar.
    // POST /Production/ReschedulePreview           { fromUtc } → önizleme (persist yok)
    // POST /Production/RescheduleApply              { fromUtc } → uygula
    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineSchedule)]
    public async Task<IActionResult> ReschedulePreview([FromBody] RescheduleRequest req, CancellationToken ct)
    {
        if (req is null)
            return Json(new { ok = false, error = "Geçersiz istek." });
        try
        {
            var fromUtc = req.FromUtc.ToUniversalTime();
            var result = await _autoSchedule.ReschedulePreviewAsync(fromUtc, ct);
            return Json(new { ok = true, proposals = result.Proposals, unplaceable = result.Unplaceable, releasedCount = result.ReleasedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoSchedule.ReschedulePreview] fromUtc={FromUtc} önizleme hesaplanamadı.", req.FromUtc);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MachineSchedule)]
    public async Task<IActionResult> RescheduleApply([FromBody] RescheduleRequest req, CancellationToken ct)
    {
        if (req is null)
            return Json(new { ok = false, error = "Geçersiz istek." });
        try
        {
            var fromUtc = req.FromUtc.ToUniversalTime();
            var result = await _autoSchedule.RescheduleApplyAsync(fromUtc, CurrentUserId(), ct);
            // Toplu özet — her blok tek tek loglanmaz (gürültü önleme, bkz. CLAUDE.md audit kuralı).
            _audit.LogEvent("MachineAutoSchedule.RescheduleApply",
                detail: $"{result.CreatedCount} blok oluşturuldu, {result.ReleasedCount} blok serbest bırakıldı, " +
                        $"{result.UnplaceableCount} operasyon yerleştirilemedi (fromUtc={fromUtc:yyyy-MM-dd HH:mm} UTC).");
            return Json(new { ok = true, createdCount = result.CreatedCount, releasedCount = result.ReleasedCount, unplaceableCount = result.UnplaceableCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoSchedule.RescheduleApply] fromUtc={FromUtc} uygulanamadı.", req.FromUtc);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ─── Kapasite / Yük Raporu — makine doluluk ısı haritası (backend, 2026-08-22) ───────
    // API sözleşmesi KİLİTLİ — frontend (Views/Production/CapacityLoad.cshtml + ısı haritası
    // bileşenleri) bu iki endpoint'e göre yazılıyor, alan adı/tipini değiştirme. Tüm zaman UTC "…Z".
    // GET /Production/CapacityLoad                         → Isı haritası view (React mount)
    // GET /Production/CapacityLoadData?from=&to=&bucket=    → kova + makine × hücre doluluk verisi
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.CapacityLoad)]
    public IActionResult CapacityLoad() => View();

    [HttpGet("Production/CapacityLoadData")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.CapacityLoad)]
    public async Task<IActionResult> CapacityLoadData(DateTime from, DateTime to, string bucket, CancellationToken ct)
    {
        try
        {
            // Frontend UTC "...Z" ISO gönderir; MVC query binder bunu yerel saate çevirip Kind=Local
            // (doğru instant, yanlış etiket) döner — MachineScheduleData ile AYNI savunma.
            var fromUtc = from.ToUniversalTime();
            var toUtc = to.ToUniversalTime();
            var result = await _capacityReport.GetCapacityLoadAsync(fromUtc, toUtc, bucket, ct);
            return Json(new { ok = true, bucket = result.Bucket, buckets = result.Buckets, machines = result.Machines });
        }
        catch (ArgumentException ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CapacityLoad.Data] from={From} to={To} bucket={Bucket} veri alınamadı.", from, to, bucket);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    private static string DayOfWeekLabel(byte d) => d switch
    {
        0 => "Pazar", 1 => "Pazartesi", 2 => "Salı", 3 => "Çarşamba", 4 => "Perşembe", 5 => "Cuma", 6 => "Cumartesi",
        _ => d.ToString(),
    };

    private static string MinuteLabel(short m) => $"{m / 60:D2}:{m % 60:D2}";

    // ─── Faz 3b: Shop-floor tablet kiosk ─────────────────────────────────────────
    // GET  /Production/ShopFloor                              → kiosk view (tek SPA)
    // POST /Production/AuthOperator                           → PIN/NFC ile Personnel doğrulama (Faz 3a-7)
    // GET  /Production/ShopFloor/Locations                    → aktif lokasyonlar (kart grid)
    // GET  /Production/ShopFloor/Machines?locationId=         → o lokasyondaki makineler + bekleyen iş sayısı
    // GET  /Production/ShopFloor/Queue?machineId=             → makine kuyruğu (Pending/InProgress)
    // POST /Production/ShopFloor/Start                        → operasyon başlat
    // POST /Production/ShopFloor/PartialComplete              → kısmi miktar gir
    // POST /Production/ShopFloor/Complete                     → operasyonu bitir
    [HttpGet]
    public IActionResult ShopFloor() => View();

    [HttpGet("Production/ShopFloor/Locations")]
    public async Task<IActionResult> ShopFloorLocations(CancellationToken ct)
    {
        var locations = await _logisticsConfig.GetLocationsAsync(ct);
        var lookup = locations.ToDictionary(l => l.Id);

        // Sadece AKTİF + KULLANIM=Makine Parkuru (IsMachinePark=true) olan lokasyonlar
        // — shop-floor terminali sadece üretim makinelerinin bulunduğu lokasyonlarda
        // anlamlıdır; depo/kabul/sevkiyat alanları operatör görünümünde olmamalı.
        var rows = locations
            .Where(l => l.IsActive && l.IsMachinePark)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.LocationCode)
            .Select(l => new
            {
                id = l.Id,
                code = l.LocationCode,
                name = l.LocationName ?? l.LocationCode,
                parentId = l.ParentId,
                parentName = l.ParentId.HasValue && lookup.TryGetValue(l.ParentId.Value, out var p)
                    ? (p.LocationName ?? p.LocationCode)
                    : (string?)null,
                typeCode = l.LocationTypeCode,
            })
            .ToArray();
        return Json(rows);
    }

    [HttpGet("Production/ShopFloor/Machines")]
    public async Task<IActionResult> ShopFloorMachines(int locationId, CancellationToken ct)
    {
        var allMachines = await _logisticsConfig.GetMachinesAsync(ct);
        var machines = allMachines
            .Where(m => m.IsActive && m.LocationId == locationId)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Code)
            .ToArray();

        var rows = new List<object>(machines.Length);
        foreach (var m in machines)
        {
            // Bekleyen/devam eden iş sayısı — pending=0, inProgress=1
            var queue = await _workOrderOperations.GetQueueByMachineAsync(m.Id, ct);
            var pending    = queue.Count(o => o.Status == Domain.Enums.WorkOrderOperationStatus.Pending);
            var inProgress = queue.Count(o => o.Status == Domain.Enums.WorkOrderOperationStatus.InProgress);
            rows.Add(new
            {
                id = m.Id,
                code = m.Code,
                name = m.Name ?? m.Code,
                pendingCount = pending,
                inProgressCount = inProgress,
                totalQueue = queue.Count,
            });
        }
        return Json(rows);
    }

    [HttpGet("Production/ShopFloor/Queue")]
    public async Task<IActionResult> ShopFloorQueue(int machineId, CancellationToken ct)
    {
        var queue = await _workOrderOperations.GetQueueByMachineAsync(machineId, ct);
        return Json(queue);
    }

    public sealed record ShopFloorStartRequest(int WorkOrderOperationId, int OperatorPersonnelId);

    [HttpPost("Production/ShopFloor/Start")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ShopFloor)]
    [CalibraHub.Web.Authorization.PermissionAction("VIEW", "VIEW_OWN")]
    public async Task<IActionResult> ShopFloorStart([FromBody] ShopFloorStartRequest req, CancellationToken ct)
    {
        try
        {
            await _workOrderOperations.StartAsync(
                new StartOperationRequest(req.WorkOrderOperationId, req.OperatorPersonnelId), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShopFloor.Start] opId={OpId} operasyon başlatılamadı.", req?.WorkOrderOperationId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record ShopFloorPartialRequest(int WorkOrderOperationId, int OperatorPersonnelId, decimal Quantity, decimal? ScrapQuantity);

    [HttpPost("Production/ShopFloor/PartialComplete")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ShopFloor)]
    [CalibraHub.Web.Authorization.PermissionAction("VIEW", "VIEW_OWN")]
    public async Task<IActionResult> ShopFloorPartialComplete([FromBody] ShopFloorPartialRequest req, CancellationToken ct)
    {
        try
        {
            await _workOrderOperations.PartialCompleteAsync(
                new PartialCompleteOperationRequest(req.WorkOrderOperationId, req.OperatorPersonnelId, req.Quantity, req.ScrapQuantity), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShopFloor.PartialComplete] opId={OpId} kısmi tamamlama yapılamadı.", req?.WorkOrderOperationId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record ShopFloorIssueComponentRequest(int WorkOrderComponentId, decimal Quantity, int OperatorPersonnelId);

    /// <summary>
    /// Malzeme Sarf Et (2026-07-02) — otomatik BOM oranından türetilmez, operatör gerçek
    /// sarfı manuel girer. IssuedQuantity artırılır + DocumentLine'a Issue satırı atomik yazılır.
    /// </summary>
    [HttpPost("Production/ShopFloor/IssueComponent")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ShopFloor)]
    [CalibraHub.Web.Authorization.PermissionAction("VIEW", "VIEW_OWN")]
    public async Task<IActionResult> ShopFloorIssueComponent([FromBody] ShopFloorIssueComponentRequest req, CancellationToken ct)
    {
        try
        {
            await _service.IssueComponentAsync(
                new IssueWorkOrderComponentRequest(req.WorkOrderComponentId, req.Quantity, req.OperatorPersonnelId), ct);
            return Json(new { ok = true });
        }
        catch (CalibraHub.Domain.Exceptions.NegativeBalanceException nbex)
        {
            // Eksi bakiye — aynı desen: IssueConsumptionJson (üretim sarfı) ve
            // Ambar Çıkış/Transfer akışları (WarehouseController/PurchaseController).
            return Json(new { ok = false, error = nbex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // IssueAsync bu tipten fırlatır: "Bileşen bulunamadı" / lot-seri takipli bileşen
            // bu yoldan sarf edilemez (bkz. SqlWorkOrderComponentRepository.IssueAsync).
            return Json(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShopFloor.IssueComponent] componentId={ComponentId} malzeme sarfı yapılamadı.", req?.WorkOrderComponentId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record ShopFloorCompleteRequest(int WorkOrderOperationId, int OperatorPersonnelId, decimal? FinalQuantity);

    [HttpPost("Production/ShopFloor/Complete")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ShopFloor)]
    [CalibraHub.Web.Authorization.PermissionAction("VIEW", "VIEW_OWN")]
    public async Task<IActionResult> ShopFloorComplete([FromBody] ShopFloorCompleteRequest req, CancellationToken ct)
    {
        try
        {
            await _workOrderOperations.CompleteAsync(
                new CompleteOperationRequest(req.WorkOrderOperationId, req.OperatorPersonnelId, req.FinalQuantity), ct);
            // Operasyon tamamlandı — aktif aktivite varsa otomatik kapat (sahanin
            // "Bitir" basmasi zaten o aktiviteyi sonlandirmis sayilir).
            await _activities.EndCurrentAsync(
                new EndActivityRequest(req.WorkOrderOperationId, req.OperatorPersonnelId, Notes: null), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShopFloor.Complete] opId={OpId} operasyon tamamlanamadı.", req?.WorkOrderOperationId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ── Faz 1 MVP (2026-05-20): Üretim sahası aktivite log ────────────────────
    // Saha tableti "Durum Değiştir" menüsünden tetikler. İki katmanlı auth:
    // 1. CalibraHub oturumu (cookie) — ShopFloor sayfasına erişim için zorunlu.
    // 2. PIN/NFC kart (AuthOperator) — her operasyon için operatör kimlik doğrulaması.

    public sealed record ShopFloorStartActivityRequest(
        int WorkOrderOperationId,
        int OperatorPersonnelId,
        byte ActivityType,
        int? ActivityReasonId,
        string? Notes);

    /// <summary>Yeni aktivite başlatır (eski aktif aktivite otomatik kapatılır).</summary>
    [HttpPost("Production/ShopFloor/StartActivity")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ShopFloor)]
    [CalibraHub.Web.Authorization.PermissionAction("VIEW", "VIEW_OWN")]
    public async Task<IActionResult> ShopFloorStartActivity(
        [FromBody] ShopFloorStartActivityRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _activities.StartAsync(new StartActivityRequest(
                WorkOrderOperationId: req.WorkOrderOperationId,
                PersonnelId:          req.OperatorPersonnelId,
                ActivityType:         (Domain.Enums.WorkOrderActivityType)req.ActivityType,
                ActivityReasonId:     req.ActivityReasonId,
                Notes:                req.Notes), ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShopFloor.StartActivity] opId={OpId} aktivite başlatılamadı.", req?.WorkOrderOperationId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record ShopFloorEndActivityRequest(
        int WorkOrderOperationId,
        int OperatorPersonnelId,
        string? Notes);

    /// <summary>Aktif aktiviteyi yeni aktivite başlatmadan kapatır.</summary>
    [HttpPost("Production/ShopFloor/EndActivity")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ShopFloor)]
    [CalibraHub.Web.Authorization.PermissionAction("VIEW", "VIEW_OWN")]
    public async Task<IActionResult> ShopFloorEndActivity(
        [FromBody] ShopFloorEndActivityRequest req, CancellationToken ct)
    {
        try
        {
            var ended = await _activities.EndCurrentAsync(
                new EndActivityRequest(req.WorkOrderOperationId, req.OperatorPersonnelId, req.Notes), ct);
            return Json(new { ok = true, ended });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ShopFloor.EndActivity] opId={OpId} aktivite kapatılamadı.", req?.WorkOrderOperationId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>Operasyonun aktif (an devam eden) aktivitesi — yoksa null döner.</summary>
    [HttpGet("Production/ShopFloor/ActiveActivity")]
    public async Task<IActionResult> ShopFloorActiveActivity(int workOrderOperationId, CancellationToken ct)
        => Json(await _activities.GetActiveAsync(workOrderOperationId, ct));

    /// <summary>Operasyonun tüm hareket geçmişi (StartedAt DESC).</summary>
    [HttpGet("Production/ShopFloor/ActivityHistory")]
    public async Task<IActionResult> ShopFloorActivityHistory(int workOrderOperationId, CancellationToken ct)
        => Json(await _activities.GetHistoryAsync(workOrderOperationId, ct));

    /// <summary>Frontend dropdown'u için aktivite tipi sözlüğü (id + label).</summary>
    [HttpGet("Production/ShopFloor/ActivityTypes")]
    public IActionResult ShopFloorActivityTypes()
    {
        var types = Enum.GetValues<Domain.Enums.WorkOrderActivityType>()
            .Select(t => new
            {
                id    = (byte)t,
                code  = t.ToString(),
                label = GetActivityTypeLabel(t),
            });
        return Json(types);
    }

    /// <summary>
    /// Kalem grid görünüm modu (kart/tablo) — kullanıcı ayarı. Üretim sarfı modalı da aynı
    /// tercihi kullanır: aksi halde modal her açılışta karta dönüyor, ama modaldeki geçiş
    /// düğmesi GLOBAL tercihi değiştirip satış/depo ekranlarını etkiliyordu (review bulgusu).
    /// Okunamazsa "card" (fail-open, mevcut davranış korunur).
    /// </summary>
    private async Task<string> GetLineViewModeAsync(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return UiConfigKeys.ViewModeCard;
        try
        {
            var raw = await _userSettings.GetAsync(userId.Value, UiConfigKeys.LineGridViewMode, ct);
            return UiConfigKeys.NormalizeViewMode(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kalem görünüm modu okunamadı (userId={UserId}) — 'card' varsayılana düşüldü.", userId);
            return UiConfigKeys.ViewModeCard;
        }
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    private static string GetActivityTypeLabel(Domain.Enums.WorkOrderActivityType type)
    {
        var member = typeof(Domain.Enums.WorkOrderActivityType)
            .GetMember(type.ToString())
            .FirstOrDefault();
        var attr = member?.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
        return attr?.Description ?? type.ToString();
    }

    // ── Faz 3a-7: Shop-floor operatör kimlik doğrulama ──────────────────────────
    // PIN veya NFC kart numarasıyla aktif üretim operatörünü bulur. Tablet
    // ekranında PIN klavyesi veya NFC reader sonrası çağrılır. Geri dönen
    // operatör kimliği frontend session/storage'a yazılır; sonraki shop-floor
    // aksiyon endpoint'lerinde body'de gönderilir.
    // 2026-05-22: Code (Sicil No) eklendi — PIN ile birlikte zorunlu (brute-force koruması).
    // NFC kart yolunda Code gerekmez — kart fiziksel sahiplik kanıtı.
    public sealed record AuthOperatorRequest(string? PersonnelCode, string? PinCode, string? CardNo);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ShopFloor)]
    [CalibraHub.Web.Authorization.PermissionAction("VIEW", "VIEW_OWN")]
    public async Task<IActionResult> AuthOperator([FromBody] AuthOperatorRequest req, CancellationToken ct)
    {
        if (req is null)
            return Json(new { ok = false, error = "Geçersiz istek." });
        var hasCard = !string.IsNullOrWhiteSpace(req.CardNo);
        var hasPin  = !string.IsNullOrWhiteSpace(req.PinCode);
        var hasCode = !string.IsNullOrWhiteSpace(req.PersonnelCode);

        if (!hasCard && !hasPin)
            return Json(new { ok = false, error = "PIN veya kart numarası girilmedi." });

        // PIN yolu artık Code zorunlu — sadece PIN ile giriş kabul edilmez
        if (!hasCard && !hasCode)
            return Json(new { ok = false, error = "Sicil numarası girilmedi. Giriş için Sicil + PIN ikisi de gerekli." });

        // ── ShopFloor PIN lockout (Code+PIN yolunda) ────────────────────────────────
        // Kart yolunda lockout uygulanmaz — fiziksel kart sahipliği zaten kanıt.
        if (!hasCard && hasCode)
        {
            var existing = await _personnelRepo.GetIdAndActiveByCodeAsync(req.PersonnelCode!, ct);
            if (existing is not null && !existing.Value.IsActive)
                return Json(new { ok = false, error = "Bu sicil bloklu. Yöneticinizle iletişime geçin." });
        }

        // Personnel tablosundan Code+PIN veya Card eşleşmesi
        var op = await _personnel.GetByPinOrCardAsync(req.PersonnelCode, req.PinCode, req.CardNo, ct);
        if (op is null)
        {
            // Yanlış PIN — Code yolunda sayacı artır; limit doluysa Personnel'i pasife al.
            if (!hasCard && hasCode)
            {
                var companyId = ResolveCurrentCompanyIdSafe();
                var limit = await GetShopFloorMaxPinAttemptsAsync(ct);
                var shouldLock = _shopFloorLockout.RegisterFailure(companyId, req.PersonnelCode!, limit);
                if (shouldLock)
                {
                    var existing = await _personnelRepo.GetIdAndActiveByCodeAsync(req.PersonnelCode!, ct);
                    if (existing is not null && existing.Value.IsActive)
                        await _personnelRepo.DeactivateAsync(existing.Value.Id, ct);
                    return Json(new { ok = false, error = $"Hatalı PIN limiti aşıldı. Sicil bloklandı, yöneticinizle iletişime geçin." });
                }
            }
            return Json(new { ok = false, error = "Operatör bulunamadı, sicil veya PIN hatalı (ya da operatör pasif)." });
        }

        // Başarılı giriş → sayacı sıfırla
        if (!string.IsNullOrWhiteSpace(op.Code))
            _shopFloorLockout.Reset(ResolveCurrentCompanyIdSafe(), op.Code);

        // Sadece minimum bilgi dön — frontend session storage'da tutar.
        return Json(new
        {
            ok = true,
            operator_ = new
            {
                id = op.Id,                       // Personnel.Id (INT) — shop-floor aksiyonlarında bu gönderilir
                fullName = op.FullName,
                code = op.Code,
                title = op.Title,
                department = op.Department,
                userId = op.UserId,                // opsiyonel: sistem kullanıcı linki varsa
            }
        });
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2026-05-21 Faz 2: Aktivite Sebepleri (ActivityReason) — admin tanım ekranı
    // GET  /Production/ActivityReasons             → SmartBoard liste
    // GET  /Production/ActivityReasonEdit?id=      → form (yeni/edit)
    // GET  /Production/ActivityReasonsList?type=   → JSON liste (ShopFloor için)
    // POST /Production/SaveActivityReason          → JSON
    // POST /Production/DeleteActivityReason/{id}   → JSON soft delete
    // ════════════════════════════════════════════════════════════════════════
    [HttpGet]
    public async Task<IActionResult> ActivityReasons(CancellationToken ct)
    {
        var board = await BuildActivityReasonsBoardConfigAsync(ct);
        ViewBag.BoardConfig = board;
        return View();
    }

    private async Task<object> BuildActivityReasonsBoardConfigAsync(CancellationToken ct)
    {
        var reasons = await _activityReasons.ListAsync(activityType: null, includeInactive: false, ct);
        var typeOptions = SmartBoardFilterHelpers.ToOptionsList(
            reasons.Select(r => r.ActivityTypeLabel).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
        var masterWidgets = new List<object>
        {
            SmartBoardFilterHelpers.MakeOptionsWidget("w_type", "Aktivite", typeOptions),
            SmartBoardFilterHelpers.MakeStdWidget   ("w_sort", "Sıra",      "numeric"),
        };
        var entities = reasons.Select(r =>
        {
            var statusBadge = r.IsActive
                ? (object)new { label = "Aktif", color = "emerald" }
                : new { label = "Pasif", color = "slate" };
            return new
            {
                id          = r.Id,
                title       = r.Name,
                subtitle    = $"{r.ActivityTypeLabel} · {r.Code}",
                description = r.Description,
                statusBadge,
                widgets = new object[]
                {
                    new { id = "w_type",     type = "data", dataType = "options", label = "Aktivite",
                          value = r.ActivityTypeLabel, color = "indigo" },
                    new { id = "w_sort",     type = "data", dataType = "numeric", label = "Sıra",
                          value = r.SortOrder.ToString(), color = "slate" },
                },
                primaryAction = new
                {
                    label = "Düzenle", icon = "Edit", color = "amber",
                    url = $"/Production/ActivityReasonEdit?id={r.Id}",
                    hideButton = true,
                },
                secondaryAction = new
                {
                    label     = "Sil", icon = "Trash2",
                    apiUrl    = $"/Production/DeleteActivityReason/{r.Id}",
                    apiMethod = "POST",
                    confirm   = $"Bu sebebi silmek istediğinize emin misiniz? ({r.Name})",
                },
            };
        }).ToArray();

        return new
        {
            boardKey          = "production-activity-reasons",
            title             = "Aktivite Sebepleri",
            subtitle          = $"{reasons.Count} sebep",
            icon              = "AlertCircle",
            iconColor         = "amber",
            refreshUrl        = "/Production/ActivityReasonsBoardConfig",
            searchPlaceholder = "Hızlı ara… (kod, ad, tip)",
            emptyText         = "Henüz sebep tanımlanmamış. Üretim sahasında operatörlerin seçeceği alt sebepleri burada tanımlarsınız.",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Sebep", icon = "Plus", variant = "primary",
                      url = "/Production/ActivityReasonEdit" },
            },
            masterWidgets,
            entities,
        };
    }

    [HttpGet]
    public async Task<IActionResult> ActivityReasonsBoardConfig(CancellationToken ct)
        => Json(await BuildActivityReasonsBoardConfigAsync(ct));

    [HttpGet]
    public async Task<IActionResult> ActivityReasonEdit(int? id, CancellationToken ct)
    {
        ActivityReasonDto? dto = null;
        if (id.HasValue && id.Value > 0)
        {
            dto = await _activityReasons.GetAsync(id.Value, ct);
            if (dto is null) return NotFound();
        }
        return View(dto);
    }

    [HttpGet]
    public async Task<IActionResult> ActivityReasonsList(byte? activityType, bool includeInactive, CancellationToken ct)
    {
        Domain.Enums.WorkOrderActivityType? typeFilter = activityType.HasValue
            ? (Domain.Enums.WorkOrderActivityType)activityType.Value
            : null;
        var list = await _activityReasons.ListAsync(typeFilter, includeInactive, ct);
        return Json(list);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ActivityReasonEdit)]
    public async Task<IActionResult> SaveActivityReason([FromBody] SaveActivityReasonRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _activityReasons.SaveAsync(req, CurrentUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ActivityReason.Save] id={Id} kaydedilemedi.", req?.Id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ActivityReasonEdit)]
    public async Task<IActionResult> DeleteActivityReason(int id, CancellationToken ct)
    {
        try
        {
            await _activityReasons.DeleteAsync(id, CurrentUserId(), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ActivityReason.Delete] id={Id} silinemedi.", id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2026-05-21 Faz 3: Vardiya (Shift) tanımları + personel atama
    // GET  /Production/Shifts                     → SmartBoard liste
    // GET  /Production/ShiftEdit?id=              → form
    // GET  /Production/ShiftsList                 → JSON
    // POST /Production/SaveShift                  → JSON
    // POST /Production/DeleteShift/{id}           → JSON soft delete
    // GET  /Production/ShiftAssignmentsList?personnelId= → JSON haftalık atama
    // POST /Production/SaveShiftAssignment        → JSON
    // POST /Production/DeleteShiftAssignment/{id} → JSON
    // ════════════════════════════════════════════════════════════════════════
    [HttpGet]
    public async Task<IActionResult> Shifts(CancellationToken ct)
    {
        var board = await BuildShiftsBoardConfigAsync(ct);
        ViewBag.BoardConfig = board;
        return View();
    }

    private async Task<object> BuildShiftsBoardConfigAsync(CancellationToken ct)
    {
        var shifts = await _shifts.ListAsync(includeInactive: true, ct);
        var typeOptions = SmartBoardFilterHelpers.ToOptionsList(new[] { "Gece", "Gündüz" });
        var masterWidgets = new List<object>
        {
            SmartBoardFilterHelpers.MakeStdWidget   ("w_start",     "Başlangıç",    "text"),
            SmartBoardFilterHelpers.MakeStdWidget   ("w_end",       "Bitiş",        "text"),
            SmartBoardFilterHelpers.MakeStdWidget   ("w_dur",       "Süre",         "numeric"),
            SmartBoardFilterHelpers.MakeOptionsWidget("w_overnight", "Tip",          typeOptions),
            SmartBoardFilterHelpers.MakeStdWidget   ("w_breaks",    "Aralar",       "text"),
            SmartBoardFilterHelpers.MakeStdWidget   ("w_net",       "Net Çalışma",  "numeric"),
        };
        var entities = shifts.Select(s =>
        {
            var statusBadge = s.IsActive
                ? (object)new { label = "Aktif", color = "emerald" }
                : new { label = "Pasif", color = "slate" };
            var hours = $"{s.StartTime} - {s.EndTime}" + (s.IsOvernight ? " (gece)" : "");
            return new
            {
                id          = s.Id,
                title       = s.Name,
                subtitle    = $"{s.Code} · {hours}",
                description = (string?)null,
                statusBadge,
                widgets = BuildShiftWidgets(s),
                primaryAction = new
                {
                    label = "Düzenle", icon = "Edit", color = "amber",
                    url = $"/Production/ShiftEdit?id={s.Id}",
                    hideButton = true,
                },
                secondaryAction = new
                {
                    label     = "Sil", icon = "Trash2",
                    apiUrl    = $"/Production/DeleteShift/{s.Id}",
                    apiMethod = "POST",
                    confirm   = $"Bu vardiyayı silmek istediğinize emin misiniz? ({s.Name})",
                },
            };
        }).ToArray();

        return new
        {
            boardKey          = "production-shifts",
            title             = "Vardiya Tanımları",
            subtitle          = $"{shifts.Count} vardiya",
            icon              = "Clock",
            iconColor         = "violet",
            refreshUrl        = "/Production/ShiftsBoardConfig",
            searchPlaceholder = "Hızlı ara… (kod, ad)",
            emptyText         = "Henüz vardiya tanımlanmamış. Gündüz / Akşam / Gece gibi vardiyaları tanımlayıp personele atayın.",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Vardiya", icon = "Plus", variant = "primary",
                      url = "/Production/ShiftEdit" },
                new { id = "assignments", label = "Atamalar", icon = "Calendar", variant = "secondary",
                      url = "/Production/ShiftAssignments" },
            },
            masterWidgets,
            entities,
        };
    }

    private static object[] BuildShiftWidgets(ShiftDto s)
    {
        var list = new List<object>
        {
            new { id = "w_start", type = "data", dataType = "text",    label = "Başlangıç", value = s.StartTime, color = "indigo" },
            new { id = "w_end",   type = "data", dataType = "text",    label = "Bitiş",     value = s.EndTime,   color = "indigo" },
            new { id = "w_dur",   type = "data", dataType = "numeric", label = "Süre",      value = $"{s.DurationMinutes / 60.0:F1} sa", color = "slate" },
            s.IsOvernight ? (object)new { id = "w_overnight", type = "data", dataType = "text", label = "Tip", value = "Gece",   color = "violet" }
                          : new { id = "w_overnight", type = "data", dataType = "text", label = "Tip", value = "Gündüz", color = "amber" },
        };
        if (s.Breaks is { Count: > 0 })
        {
            list.Add(new
            {
                id       = "w_breaks",
                type     = "data",
                dataType = "text",
                label    = "Aralar",
                value    = $"{s.Breaks.Count} ara · {s.TotalBreakMinutes} dk",
                color    = "amber",
            });
            list.Add(new
            {
                id       = "w_net",
                type     = "data",
                dataType = "numeric",
                label    = "Net Çalışma",
                value    = $"{s.NetWorkMinutes / 60.0:F1} sa",
                color    = "emerald",
            });
        }
        return list.ToArray();
    }

    [HttpGet]
    public async Task<IActionResult> ShiftsBoardConfig(CancellationToken ct)
        => Json(await BuildShiftsBoardConfigAsync(ct));

    [HttpGet]
    public async Task<IActionResult> ShiftEdit(int? id, CancellationToken ct)
    {
        ShiftDto? dto = null;
        if (id.HasValue && id.Value > 0)
        {
            dto = await _shifts.GetAsync(id.Value, ct);
            if (dto is null) return NotFound();
        }
        return View(dto);
    }

    [HttpGet]
    public async Task<IActionResult> ShiftsList(bool includeInactive, CancellationToken ct)
        => Json(await _shifts.ListAsync(includeInactive, ct));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ShiftEdit)]
    public async Task<IActionResult> SaveShift([FromBody] SaveShiftRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _shifts.SaveAsync(req, CurrentUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Shift.Save] id={Id} vardiya kaydedilemedi.", req?.Id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ShiftEdit)]
    public async Task<IActionResult> DeleteShift(int id, CancellationToken ct)
    {
        try
        {
            await _shifts.DeleteAsync(id, CurrentUserId(), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Shift.Delete] id={Id} vardiya silinemedi.", id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ── Atama (matrix UI sayfası — tüm personel × 7 gün) ──
    [HttpGet]
    public async Task<IActionResult> ShiftAssignments(CancellationToken ct)
    {
        // View'a personel + vardiya listesi + tüm aktif atamalar geçer.
        var personnel = await _personnel.ListAsync(includeInactive: false, onlyOperators: false, ct);
        var shifts    = await _shifts.ListAsync(includeInactive: false, ct);
        var allAssignments = new List<ShiftAssignmentDto>();
        foreach (var p in personnel)
            allAssignments.AddRange(await _shiftAssignments.GetByPersonnelAsync(p.Id, ct));
        ViewBag.Personnel = personnel;
        ViewBag.Shifts = shifts;
        ViewBag.Assignments = allAssignments;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> ShiftAssignmentsList(int personnelId, CancellationToken ct)
        => Json(await _shiftAssignments.GetByPersonnelAsync(personnelId, ct));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ShiftEdit)]
    public async Task<IActionResult> SaveShiftAssignment(
        [FromBody] SaveShiftAssignmentRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _shiftAssignments.SaveAsync(req, CurrentUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Shift.SaveAssignment] personnelId={PersonnelId} vardiya ataması kaydedilemedi.", req?.PersonnelId);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ShiftEdit)]
    public async Task<IActionResult> DeleteShiftAssignment(int id, CancellationToken ct)
    {
        try
        {
            await _shiftAssignments.DeleteAsync(id, CurrentUserId(), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Shift.DeleteAssignment] id={Id} vardiya ataması silinemedi.", id);
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> CurrentShift(int personnelId, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(DateTime.Now);
        var current = await _shiftAssignments.GetCurrentAsync(personnelId, date, ct);
        return Json(current);
    }
}
