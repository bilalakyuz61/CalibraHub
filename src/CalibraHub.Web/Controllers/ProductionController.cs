using System.Globalization;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
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

    public ProductionController(
        IWorkOrderService service,
        IOperationService operations,
        IRoutingService routings,
        IOperationMachineTimeService machineTimes,
        IWorkOrderOperationService workOrderOperations,
        IPersonnelService personnel,
        IWidgetService widgetService,
        ILogisticsConfigurationService logisticsConfig)
    {
        _service = service;
        _operations = operations;
        _routings = routings;
        _machineTimes = machineTimes;
        _workOrderOperations = workOrderOperations;
        _personnel = personnel;
        _widgetService = widgetService;
        _logisticsConfig = logisticsConfig;
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
        var trCulture = CultureInfo.GetCultureInfo("tr-TR");

        // Master widget şablonu — admin SmartBoardConfigPanel için
        var masterWidgets = new List<object>();
        var schema = await _widgetService.GetFormSchemaByCodeAsync("WORK_ORDER_EDIT", ct);
        if (schema != null)
        {
            foreach (var w in schema.Widgets.Where(w => w.IsActive
                && !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(w.DataType, "grid",  StringComparison.OrdinalIgnoreCase)))
            {
                masterWidgets.Add(new
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
            if (!string.IsNullOrWhiteSpace(o.AssignedUserName))
            {
                widgets.Add(new { id = "w_assigned", type = "data", dataType = "text",
                    label = "Sorumlu", value = o.AssignedUserName, detail = (string?)null, color = "slate" });
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

    private static string WorkOrderStatusLabel(WorkOrderStatus s) => s switch
    {
        WorkOrderStatus.Planned => "Taslak",
        WorkOrderStatus.Released => "Salındı",
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
            return Json(new { ok = false, error = ex.Message });
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
            return Json(new { ok = false, error = ex.Message });
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
            return Json(new { ok = false, error = ex.Message });
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
            return Json(new { ok = false, error = ex.Message });
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
            return Json(new { ok = false, error = ex.Message });
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
    public async Task<IActionResult> Operations(CancellationToken ct)
    {
        var boardConfig = await BuildOperationsBoardConfigAsync(ct);
        return View(new OperationsViewModel { BoardConfig = boardConfig });
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

        var masterWidgets = new List<object>();
        var schema = await _widgetService.GetFormSchemaByCodeAsync("OPERATION_EDIT", ct);
        if (schema != null)
        {
            foreach (var w in schema.Widgets.Where(w => w.IsActive
                && !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(w.DataType, "grid",  StringComparison.OrdinalIgnoreCase)))
            {
                masterWidgets.Add(new
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

        var recordIds = ops.Select(o => o.Id.ToString()).ToArray();
        var batchWidgets = masterWidgets.Count > 0 && recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("OPERATION_EDIT", recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        var entities = new List<object>();
        foreach (var o in ops)
        {
            var widgets = new List<object>();
            widgets.Add(new { id = "w_active", type = "data", dataType = "text",
                label = "Durum", value = o.IsActive ? "Aktif" : "Pasif", detail = (string?)null,
                color = o.IsActive ? "emerald" : "slate" });

            if (o.StandardDuration.HasValue)
            {
                var unit = o.DurationUnit == DurationUnit.Hour ? "saat" : "dk";
                widgets.Add(new { id = "w_duration", type = "data", dataType = "numeric",
                    label = "Std. Süre", value = o.StandardDuration.Value.ToString("N2", trCulture),
                    detail = unit, color = "indigo" });
            }

            if (o.HourlyRate.HasValue)
            {
                widgets.Add(new { id = "w_rate", type = "data", dataType = "currency",
                    label = "Saatlik Ücret", value = o.HourlyRate.Value.ToString("N2", trCulture),
                    detail = "TL/saat", color = "blue" });
            }

            // Dinamik widget'lar
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

            entities.Add(new
            {
                id = o.Id,
                title = o.Name,
                subtitle = o.Code,
                description = o.Description ?? string.Empty,
                imageUrl = (string?)null,
                statusBadge = (object?)null,
                widgets,
                primaryAction = new
                {
                    label = "Düzenle",
                    icon = "Edit",
                    color = "amber",
                    url = $"/Production/OperationEdit?id={o.Id}",
                    hideButton = true,
                },
                secondaryAction = new
                {
                    label = "Sil",
                    icon = "Trash2",
                    apiUrl = $"/Production/DeleteOperation/{o.Id}",
                    apiMethod = "POST",
                    confirm = $"Bu operasyonu silmek istediğinize emin misiniz? ({o.Code})",
                },
            });
        }

        return new
        {
            boardKey = "production-operations",
            title = "Operasyon Tanımlamaları",
            subtitle = $"{entities.Count} operasyon",
            icon = "Hammer",
            iconColor = "indigo",
            searchPlaceholder = "Hızlı ara... (kod, ad)",
            emptyText = "Henüz operasyon tanımlanmamış",
            actions = new object[]
            {
                new
                {
                    id = "new",
                    label = "Yeni Operasyon",
                    icon = "Plus",
                    variant = "primary",
                    url = "/Production/OperationEdit",
                },
            },
            masterWidgets,
            entities,
        };
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
            return Json(new { ok = false, error = ex.Message });
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
            return Json(new { ok = false, error = ex.Message });
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

    // ── Routing CRUD ekranı (Faz 3a-2) ──────────────────────────────────────
    // GET  /Production/Routings                  → SmartBoard liste view
    // GET  /Production/RoutingEdit?id=           → master-detail form (yeni/edit)
    // GET  /Production/RoutingsList?itemId=      → JSON liste (filtreli)
    // GET  /Production/Routing/{id}              → JSON tekil (header + operations)
    // POST /Production/SaveRouting               → JSON (id=0 yeni, id>0 update — header + operations)
    // POST /Production/DeleteRouting/{id}        → JSON
    [HttpGet]
    public async Task<IActionResult> Routings(CancellationToken ct)
    {
        var boardConfig = await BuildRoutingsBoardConfigAsync(ct);
        return View(new RoutingsViewModel { BoardConfig = boardConfig });
    }

    private async Task<object> BuildRoutingsBoardConfigAsync(CancellationToken ct)
    {
        var routings = await _routings.ListAsync(itemId: null, ct);

        // Master widget şablonu — admin SmartBoardConfigPanel için
        var masterWidgets = new List<object>();
        var schema = await _widgetService.GetFormSchemaByCodeAsync("ROUTING_EDIT", ct);
        if (schema != null)
        {
            foreach (var w in schema.Widgets.Where(w => w.IsActive
                && !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(w.DataType, "grid",  StringComparison.OrdinalIgnoreCase)))
            {
                masterWidgets.Add(new
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

            entities.Add(new
            {
                id = r.Id,
                title = r.Name,
                subtitle = r.Code,
                description = r.Description ?? string.Empty,
                imageUrl = (string?)null,
                statusBadge = (object?)null,
                widgets,
                primaryAction = new
                {
                    label = "Düzenle",
                    icon = "Edit",
                    color = "amber",
                    url = $"/Production/RoutingEdit?id={r.Id}",
                    hideButton = true,
                },
                secondaryAction = new
                {
                    label = "Sil",
                    icon = "Trash2",
                    apiUrl = $"/Production/DeleteRouting/{r.Id}",
                    apiMethod = "POST",
                    confirm = $"Bu rotayı silmek istediğinize emin misiniz? ({r.Code})",
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
            searchPlaceholder = "Hızlı ara... (kod, ad, mamul)",
            emptyText = "Henüz rota tanımlanmamış",
            actions = new object[]
            {
                new
                {
                    id = "new",
                    label = "Yeni Rota",
                    icon = "Plus",
                    variant = "primary",
                    url = "/Production/RoutingEdit",
                },
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
            return Json(new { ok = false, error = ex.Message });
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
            return Json(new { ok = false, error = ex.Message });
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
            return Json(new { ok = false, error = ex.Message });
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
            return Json(new { ok = false, error = ex.Message });
        }
    }

    // ── Personnel (Faz 3a — uretim personeli kartlari) ────────────────────────
    // GET  /Production/Personnel                  → SmartBoard liste view
    // GET  /Production/PersonnelEdit?id=          → master-detail form (yeni/edit)
    // GET  /Production/PersonnelList?...          → JSON liste (filtreli)
    // GET  /Production/PersonnelById/{id}         → JSON tekil
    // POST /Production/SavePersonnel              → JSON (id=0 yeni, id>0 update)
    // POST /Production/DeletePersonnel/{id}       → JSON
    [HttpGet]
    public async Task<IActionResult> Personnel(CancellationToken ct)
    {
        var boardConfig = await BuildPersonnelBoardConfigAsync(ct);
        return View(new PersonnelViewModel { BoardConfig = boardConfig });
    }

    private async Task<object> BuildPersonnelBoardConfigAsync(CancellationToken ct)
    {
        var people = await _personnel.ListAsync(includeInactive: true, onlyOperators: false, ct);

        // Master widget şablonu — Operations.cshtml ile aynı dinamik widget desteği
        var masterWidgets = new List<object>();
        var schema = await _widgetService.GetFormSchemaByCodeAsync("PERSONNEL_EDIT", ct);
        if (schema != null)
        {
            foreach (var w in schema.Widgets.Where(w => w.IsActive
                && !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(w.DataType, "grid",  StringComparison.OrdinalIgnoreCase)))
            {
                masterWidgets.Add(new
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

        var recordIds = people.Select(p => p.Id.ToString()).ToArray();
        var batchWidgets = masterWidgets.Count > 0 && recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("PERSONNEL_EDIT", recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        var entities = new List<object>();
        foreach (var p in people)
        {
            var widgets = new List<object>();

            // Sistem widget'lari
            widgets.Add(new
            {
                id = "w_active", type = "data", dataType = "text",
                label = "Durum", value = p.IsActive ? "Aktif" : "Pasif", detail = (string?)null,
                color = p.IsActive ? "emerald" : "slate"
            });

            if (p.IsProductionOperator)
            {
                widgets.Add(new
                {
                    id = "w_operator", type = "data", dataType = "text",
                    label = "Üretim Operatörü", value = "Evet", detail = (string?)null,
                    color = "indigo"
                });
            }

            if (!string.IsNullOrWhiteSpace(p.Title))
            {
                widgets.Add(new
                {
                    id = "w_title", type = "data", dataType = "text",
                    label = "Ünvan", value = p.Title!, detail = (string?)null,
                    color = "slate"
                });
            }

            if (!string.IsNullOrWhiteSpace(p.Department))
            {
                widgets.Add(new
                {
                    id = "w_dept", type = "data", dataType = "text",
                    label = "Departman", value = p.Department!, detail = (string?)null,
                    color = "blue"
                });
            }

            if (!string.IsNullOrWhiteSpace(p.PinCode))
            {
                widgets.Add(new
                {
                    id = "w_pin", type = "data", dataType = "text",
                    label = "PIN", value = "•••••", detail = "Tablet girişi",
                    color = "amber"
                });
            }

            if (!string.IsNullOrWhiteSpace(p.CardNo))
            {
                widgets.Add(new
                {
                    id = "w_card", type = "data", dataType = "text",
                    label = "Kart No", value = p.CardNo!, detail = "NFC",
                    color = "rose"
                });
            }

            // Dinamik widget'lar
            var recordId = p.Id.ToString();
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

            entities.Add(new
            {
                id = p.Id,
                title = p.FullName,
                subtitle = p.Code,
                description = p.Title ?? p.Department ?? string.Empty,
                imageUrl = (string?)null,
                statusBadge = (object?)null,
                widgets,
                primaryAction = new
                {
                    label = "Düzenle",
                    icon = "Edit",
                    color = "amber",
                    url = $"/Production/PersonnelEdit?id={p.Id}",
                    hideButton = true,
                },
                secondaryAction = new
                {
                    label = "Sil",
                    icon = "Trash2",
                    apiUrl = $"/Production/DeletePersonnel/{p.Id}",
                    apiMethod = "POST",
                    confirm = $"Bu personeli silmek istediğinize emin misiniz? ({p.FullName})",
                },
            });
        }

        return new
        {
            boardKey = "production-personnel",
            title = "Personel Tanımlamaları",
            subtitle = $"{entities.Count} personel",
            icon = "Users",
            iconColor = "indigo",
            searchPlaceholder = "Hızlı ara... (kod, ad, departman, ünvan)",
            emptyText = "Henüz personel tanımlanmamış",
            actions = new object[]
            {
                new
                {
                    id = "new",
                    label = "Yeni Personel",
                    icon = "Plus",
                    variant = "primary",
                    url = "/Production/PersonnelEdit",
                },
            },
            masterWidgets,
            entities,
        };
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
            return Json(new { ok = false, error = ex.Message });
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
            return Json(new { ok = false, error = ex.Message });
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
            return Json(new { ok = false, error = ex.Message });
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
            return Json(new { ok = false, error = ex.Message });
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
            return Json(new { ok = false, error = ex.Message });
        }
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
    [AllowAnonymous]
    public IActionResult ShopFloor() => View();

    [HttpGet("Production/ShopFloor/Locations")]
    [AllowAnonymous]
    public async Task<IActionResult> ShopFloorLocations(CancellationToken ct)
    {
        var locations = await _logisticsConfig.GetLocationsAsync(ct);
        var lookup = locations.ToDictionary(l => l.Id);

        // Sadece aktif + bu lokasyon altında en az bir aktif makine VEYA bir alt lokasyonu olanlar görünür.
        // Hiyerarşi yerine düz liste — operatör doğrudan makineye gidecek.
        var rows = locations
            .Where(l => l.IsActive)
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
    [AllowAnonymous]
    public async Task<IActionResult> ShopFloorMachines(int locationId, CancellationToken ct)
    {
        var allMachines = await _logisticsConfig.GetMachinesAsync(ct);
        var machines = allMachines
            .Where(m => m.IsActive && m.LocationId == locationId)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.MachineCode)
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
                code = m.MachineCode,
                name = m.MachineName ?? m.MachineCode,
                machineType = m.MachineType,
                pendingCount = pending,
                inProgressCount = inProgress,
                totalQueue = queue.Count,
            });
        }
        return Json(rows);
    }

    [HttpGet("Production/ShopFloor/Queue")]
    [AllowAnonymous]
    public async Task<IActionResult> ShopFloorQueue(int machineId, CancellationToken ct)
    {
        var queue = await _workOrderOperations.GetQueueByMachineAsync(machineId, ct);
        return Json(queue);
    }

    public sealed record ShopFloorStartRequest(int WorkOrderOperationId, int OperatorPersonnelId);

    [HttpPost("Production/ShopFloor/Start")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ShopFloorStart([FromBody] ShopFloorStartRequest req, CancellationToken ct)
    {
        try
        {
            await _workOrderOperations.StartAsync(
                new StartOperationRequest(req.WorkOrderOperationId, req.OperatorPersonnelId), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    public sealed record ShopFloorPartialRequest(int WorkOrderOperationId, int OperatorPersonnelId, decimal Quantity, decimal? ScrapQuantity);

    [HttpPost("Production/ShopFloor/PartialComplete")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ShopFloorPartialComplete([FromBody] ShopFloorPartialRequest req, CancellationToken ct)
    {
        try
        {
            await _workOrderOperations.PartialCompleteAsync(
                new PartialCompleteOperationRequest(req.WorkOrderOperationId, req.OperatorPersonnelId, req.Quantity, req.ScrapQuantity), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    public sealed record ShopFloorCompleteRequest(int WorkOrderOperationId, int OperatorPersonnelId, decimal? FinalQuantity);

    [HttpPost("Production/ShopFloor/Complete")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ShopFloorComplete([FromBody] ShopFloorCompleteRequest req, CancellationToken ct)
    {
        try
        {
            await _workOrderOperations.CompleteAsync(
                new CompleteOperationRequest(req.WorkOrderOperationId, req.OperatorPersonnelId, req.FinalQuantity), ct);
            return Json(new { ok = true });
        }
        catch (Exception ex) { return Json(new { ok = false, error = ex.Message }); }
    }

    // ── Faz 3a-7: Shop-floor operatör kimlik doğrulama ──────────────────────────
    // PIN veya NFC kart numarasıyla aktif üretim operatörünü bulur. Tablet
    // ekranında PIN klavyesi veya NFC reader sonrası çağrılır. Geri dönen
    // operatör kimliği frontend session/storage'a yazılır; sonraki shop-floor
    // aksiyon endpoint'lerinde body'de gönderilir.
    public sealed record AuthOperatorRequest(string? PinCode, string? CardNo);

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AuthOperator([FromBody] AuthOperatorRequest req, CancellationToken ct)
    {
        if (req is null || (string.IsNullOrWhiteSpace(req.PinCode) && string.IsNullOrWhiteSpace(req.CardNo)))
            return Json(new { ok = false, error = "PIN veya kart numarası girilmedi." });

        // Personnel tablosundan PIN/Kart eşleşmesi (per-company SqlServerConnectionFactory ile zaten izole).
        var op = await _personnel.GetByPinOrCardAsync(req.PinCode, req.CardNo, ct);
        if (op is null)
            return Json(new { ok = false, error = "Operatör bulunamadı, kimlik bilgileri yanlış veya operatör pasif." });

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
}
