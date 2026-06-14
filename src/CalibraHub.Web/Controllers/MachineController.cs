using CalibraHub.Application.Constants;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Logistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// MachineController — Makine Tanimlamalari aggregate'inin ayri controller'i (rapor §2.3 pilot).
///
/// LogisticsController.cs (5296 satir, 89 endpoint) icinden Machine ile ilgili 5 endpoint
/// + 1 helper bu controller'a tasindi:
///   - GET  /Logistics/Machines               → SmartBoard liste
///   - GET  /Logistics/MachineEdit            → master-detail form
///   - GET  /Logistics/GetAllMachines         → JSON liste (filtreli)
///   - POST /Logistics/SaveMachineJson        → JSON (insert/update)
///   - POST /Logistics/DeleteMachineJson      → JSON (soft delete)
///
/// URL preservation: Route attribute ile eski /Logistics/Xxx URL'leri **AYNEN** korunur.
/// Frontend tarafindan hicbir cagri kirilmaz.
///
/// DI: 2 service (vs LogisticsController'in 7 service'i). Test edilmesi cok daha kolay.
/// </summary>
[Authorize]
[Route("Logistics/[action]")]   // URL preservation: eski /Logistics/Machines aynen calisir
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.Machines)]
public sealed class MachineController : Controller
{
    private readonly ILogisticsConfigurationService _logisticsConfigurationService;
    private readonly IWidgetService _widgetService;

    public MachineController(
        ILogisticsConfigurationService logisticsConfigurationService,
        IWidgetService widgetService)
    {
        _logisticsConfigurationService = logisticsConfigurationService;
        _widgetService = widgetService;
    }

    // ── Liste sayfasi + SmartBoard config ─────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Machines(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        // Explicit view path — split sonrasi view'lar /Views/Logistics/ altinda kaldi (rapor §2.3 split-fixup).
        return View("~/Views/Logistics/Machines.cshtml", new MachinesSmartBoardViewModel { BoardConfig = config });
    }

    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        // Veri toplama
        var machines = await _logisticsConfigurationService.GetMachinesAsync(ct);
        var masterWidgets = new List<object>();
        var schema = await _widgetService.GetFormSchemaByCodeAsync("MACHINES", ct);
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
        var ordered      = machines.OrderBy(m => m.SortOrder).ThenBy(m => m.Name).ToList();
        var recordIds    = ordered.Select(m => m.Id.ToString()).ToArray();
        var batchWidgets = masterWidgets.Count > 0 && recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("MACHINES", recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        // SmartBoardBuilder ile fluent API (rapor §2.5)
        return CalibraHub.Application.SmartBoard.SmartBoard.For(ordered)
            .WithBoardKey("logistics-machines")
            .WithTitle("Makine Tanımlamaları", subtitle: $"{ordered.Count} makine")
            .WithIcon("Cog", "indigo")
            .WithSearchPlaceholder("Hızlı ara… (ad, lokasyon)")
            .WithEmptyText("Henüz makine tanımlanmamış")
            .AddHeaderAction("new", "Yeni Makine", "Plus", "/Logistics/MachineEdit")
            .WithMasterWidgets(masterWidgets)
            .MapEntities(m =>
            {
                var displayName = m.Name ?? m.Code;
                var description = m.LocationCode != null
                    ? m.LocationCode + (m.LocationName != null ? " — " + m.LocationName : "")
                    : string.Empty;

                var eb = CalibraHub.Application.SmartBoard.SmartBoardEntity
                    .For(m.Id, displayName)
                    .WithDescription(description)
                    .AddStatusWidget("w_status", "Durum", m.IsActive);

                if (batchWidgets.TryGetValue(m.Id.ToString(), out var dtos))
                {
                    eb.AppendWidgets(dtos.Select(w => (object)new
                    {
                        id           = w.WidgetId,
                        type         = "data",
                        dataType     = w.DataType.ToLowerInvariant(),
                        label        = w.Label,
                        value        = w.Value,
                        isPlainField = w.IsPlainField,
                        detail       = (string?)null,
                        color        = (string?)null,
                    }));
                }

                return eb.WithEditAndDelete(
                    editUrl:       $"/Logistics/MachineEdit?id={m.Id}",
                    deleteApiUrl:  $"/Logistics/DeleteMachineJson?id={m.Id}",
                    deleteConfirm: $"Bu makineyi silmek istediğinize emin misiniz? ({displayName})");
            })
            .Build();
    }

    // ── Edit form ─────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> MachineEdit(int? id, CancellationToken ct)
    {
        if (id.HasValue)
        {
            var all  = await _logisticsConfigurationService.GetMachinesAsync(ct);
            var item = all.FirstOrDefault(m => m.Id == id.Value);
            if (item is null) return NotFound();
            return View("~/Views/Logistics/MachineEdit.cshtml", new MachineEditViewModel
            {
                Id          = item.Id,
                LocationId  = item.LocationId,
                MachineCode = item.Code,
                MachineName = item.Name,
                SortOrder   = item.SortOrder,
                IsActive    = item.IsActive,
            });
        }
        return View("~/Views/Logistics/MachineEdit.cshtml", new MachineEditViewModel { IsActive = true });
    }

    // ── JSON API ──────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAllMachines(string? search, CancellationToken ct)
    {
        var rows = await _logisticsConfigurationService.GetMachinesAsync(ct);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim().ToLowerInvariant();
            rows = rows.Where(m =>
                (m.Code ?? "").ToLowerInvariant().Contains(q) ||
                (m.Name ?? "").ToLowerInvariant().Contains(q) ||
                (m.LocationCode ?? "").ToLowerInvariant().Contains(q) ||
                (m.LocationName ?? "").ToLowerInvariant().Contains(q)).ToArray();
        }
        return Json(rows);
    }

    [HttpPost]
    public async Task<IActionResult> SaveMachineJson([FromBody] MachineInput input, CancellationToken ct)
    {
        if (input is null)
            return Json(new { success = false, message = "Gecersiz istek." });
        // MachineCode UI'da gosterilmiyor — Service tarafinda otomatik uretiliyor.
        if (input.LocationId <= 0)
            return Json(new { success = false, message = "Lokasyon secimi zorunludur." });

        try
        {
            if (input.Id.HasValue && input.Id.Value > 0)
            {
                await _logisticsConfigurationService.UpdateMachineAsync(
                    new UpdateMachineRequest(input.Id.Value, input.LocationId, input.MachineCode,
                        input.MachineName, input.HourlyCapacity,
                        input.SortOrder, input.IsActive), ct);
                return Json(new { success = true, id = input.Id.Value });
            }
            else
            {
                var newId = await _logisticsConfigurationService.CreateMachineAsync(
                    new CreateMachineRequest(input.LocationId, input.MachineCode,
                        input.MachineName, input.HourlyCapacity,
                        input.SortOrder, input.IsActive), ct);
                return Json(new { success = true, id = newId });
            }
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteMachineJson(int id, CancellationToken ct)
    {
        try
        {
            await _logisticsConfigurationService.DeleteMachineAsync(id, ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// SaveMachineJson body — eski LogisticsController'da nested class'ti, ayni shape.
    /// </summary>
    public sealed class MachineInput
    {
        public int? Id { get; set; }
        public int LocationId { get; set; }
        public string MachineCode { get; set; } = string.Empty;
        public string? MachineName { get; set; }
        public decimal? HourlyCapacity { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
