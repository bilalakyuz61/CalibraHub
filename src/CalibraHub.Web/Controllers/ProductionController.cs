using CalibraHub.Application.Constants;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Helpers;
using CalibraHub.Web.Models.Production;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    public ProductionController(
        IWorkOrderService service,
        IOperationService operations,
        IRoutingService routings,
        IOperationMachineTimeService machineTimes,
        IWorkOrderOperationService workOrderOperations,
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
        CalibraHub.Persistence.Database.SqlServerConnectionFactory connectionFactory)
    {
        _service = service;
        _operations = operations;
        _routings = routings;
        _machineTimes = machineTimes;
        _workOrderOperations = workOrderOperations;
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
    }

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

    [HttpGet]
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
                    label = "Plan Bitiş", value = o.PlannedEndDate.Value.ToLocalTime().ToString("dd.MM.yyyy", trCulture),
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
    [HttpGet]
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
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateWorkOrderRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _service.CreateAsync(req, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateWorkOrderRequest req, CancellationToken ct)
    {
        try
        {
            await _service.UpdateAsync(id, req, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(int id, [FromBody] ChangeWorkOrderStatusRequest req, CancellationToken ct)
    {
        try
        {
            await _service.ChangeStatusAsync(id, req.NewStatus, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revise(int id, CancellationToken ct)
    {
        try
        {
            var newId = await _service.ReviseAsync(id, ct);
            return Json(new { ok = true, id = newId });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFromSalesLine([FromBody] CreateWorkOrderFromSalesLineRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _service.CreateFromSalesLineAsync(req, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
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
    public async Task<IActionResult> SaveOperation([FromBody] SaveOperationRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _operations.SaveAsync(req, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteOperation(int id, CancellationToken ct)
    {
        try
        {
            await _operations.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
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
                    label = "Mamul", value = "�?ablon", detail = "Item bağı yok",
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
    public async Task<IActionResult> SaveRouting([FromBody] SaveRoutingRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _routings.SaveAsync(req, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRouting(int id, CancellationToken ct)
    {
        try
        {
            await _routings.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
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
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost("/Production/DeleteRoutingItemMap")]
    [ValidateAntiForgeryToken]
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
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ── Operation × Machine süre eşleştirmeleri ───────────────────────────────
    [HttpGet]
    public async Task<IActionResult> OperationMachineTimes(int operationId, CancellationToken ct)
    {
        var list = await _machineTimes.ListByOperationAsync(operationId, ct);
        return Json(list);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveOperationMachineTime([FromBody] SaveOperationMachineTimeRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _machineTimes.SaveAsync(req, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteOperationMachineTime(int id, CancellationToken ct)
    {
        try
        {
            await _machineTimes.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
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
    public async Task<IActionResult> SavePersonnel([FromBody] SavePersonnelRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _personnel.SaveAsync(req, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePersonnel(int id, CancellationToken ct)
    {
        try
        {
            await _personnel.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
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
    public async Task<IActionResult> SaveWorkOrderOperation([FromBody] SaveWorkOrderOperationRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _workOrderOperations.SaveAsync(req, ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteWorkOrderOperation(int id, CancellationToken ct)
    {
        try
        {
            await _workOrderOperations.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    public sealed record ExplodeFromRoutingRequest(int WorkOrderId, int RoutingId);

    [HttpPost]
    [ValidateAntiForgeryToken]
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
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ─── Faz 2: BOM Patlatma (WorkOrderComponent) ────────────────────────────────
    // POST /Production/ExplodeBom/{workOrderId}                → reçeteyi patlat (idempotent)
    // GET  /Production/WorkOrderComponents?workOrderId=        → patlatılmış bileşen listesi
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExplodeBom(int workOrderId, CancellationToken ct)
    {
        try
        {
            var result = await _service.ExplodeBomAsync(workOrderId, ct);
            return Json(new { ok = true, result });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> WorkOrderComponents(int workOrderId, CancellationToken ct)
    {
        var list = await _service.GetComponentsAsync(workOrderId, ct);
        return Json(list);
    }

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
    public async Task<IActionResult> ShopFloorStart([FromBody] ShopFloorStartRequest req, CancellationToken ct)
    {
        try
        {
            await _workOrderOperations.StartAsync(
                new StartOperationRequest(req.WorkOrderOperationId, req.OperatorPersonnelId), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex) { return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." }); }
    }

    public sealed record ShopFloorPartialRequest(int WorkOrderOperationId, int OperatorPersonnelId, decimal Quantity, decimal? ScrapQuantity);

    [HttpPost("Production/ShopFloor/PartialComplete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ShopFloorPartialComplete([FromBody] ShopFloorPartialRequest req, CancellationToken ct)
    {
        try
        {
            await _workOrderOperations.PartialCompleteAsync(
                new PartialCompleteOperationRequest(req.WorkOrderOperationId, req.OperatorPersonnelId, req.Quantity, req.ScrapQuantity), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex) { return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." }); }
    }

    public sealed record ShopFloorIssueComponentRequest(int WorkOrderComponentId, decimal Quantity, int OperatorPersonnelId);

    /// <summary>
    /// Malzeme Sarf Et (2026-07-02) — otomatik BOM oranından türetilmez, operatör gerçek
    /// sarfı manuel girer. IssuedQuantity artırılır + DocumentLine'a Issue satırı atomik yazılır.
    /// </summary>
    [HttpPost("Production/ShopFloor/IssueComponent")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ShopFloorIssueComponent([FromBody] ShopFloorIssueComponentRequest req, CancellationToken ct)
    {
        try
        {
            await _service.IssueComponentAsync(
                new IssueWorkOrderComponentRequest(req.WorkOrderComponentId, req.Quantity, req.OperatorPersonnelId), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex) { return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." }); }
    }

    public sealed record ShopFloorCompleteRequest(int WorkOrderOperationId, int OperatorPersonnelId, decimal? FinalQuantity);

    [HttpPost("Production/ShopFloor/Complete")]
    [ValidateAntiForgeryToken]
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
        catch (Exception ex) { return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." }); }
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
        catch (Exception ex) { return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." }); }
    }

    public sealed record ShopFloorEndActivityRequest(
        int WorkOrderOperationId,
        int OperatorPersonnelId,
        string? Notes);

    /// <summary>Aktif aktiviteyi yeni aktivite başlatmadan kapatır.</summary>
    [HttpPost("Production/ShopFloor/EndActivity")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ShopFloorEndActivity(
        [FromBody] ShopFloorEndActivityRequest req, CancellationToken ct)
    {
        try
        {
            var ended = await _activities.EndCurrentAsync(
                new EndActivityRequest(req.WorkOrderOperationId, req.OperatorPersonnelId, req.Notes), ct);
            return Json(new { ok = true, ended });
        }
        catch (Exception ex) { return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." }); }
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
    public async Task<IActionResult> SaveActivityReason([FromBody] SaveActivityReasonRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _activityReasons.SaveAsync(req, CurrentUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex) { return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteActivityReason(int id, CancellationToken ct)
    {
        try
        {
            await _activityReasons.DeleteAsync(id, CurrentUserId(), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex) { return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." }); }
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
    public async Task<IActionResult> SaveShift([FromBody] SaveShiftRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _shifts.SaveAsync(req, CurrentUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex) { return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteShift(int id, CancellationToken ct)
    {
        try
        {
            await _shifts.DeleteAsync(id, CurrentUserId(), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex) { return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." }); }
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
    public async Task<IActionResult> SaveShiftAssignment(
        [FromBody] SaveShiftAssignmentRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _shiftAssignments.SaveAsync(req, CurrentUserId(), ct);
            return Json(new { ok = true, id });
        }
        catch (Exception ex) { return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteShiftAssignment(int id, CancellationToken ct)
    {
        try
        {
            await _shiftAssignments.DeleteAsync(id, CurrentUserId(), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex) { return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." }); }
    }

    [HttpGet]
    public async Task<IActionResult> CurrentShift(int personnelId, CancellationToken ct)
    {
        var date = DateOnly.FromDateTime(DateTime.Now);
        var current = await _shiftAssignments.GetCurrentAsync(personnelId, date, ct);
        return Json(current);
    }
}
