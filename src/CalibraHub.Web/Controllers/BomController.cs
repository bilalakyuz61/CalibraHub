using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models.Logistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// BomController — Urun Agaci (BOM / Recete) aggregate'i (rapor §2.3 split).
///
/// Tasinmis endpoint'ler:
///   - GET  /Logistics/BOMs                → SmartBoard liste + backwards-compat redirect (?mat=X)
///   - GET  /Logistics/BOMEdit             → React BOM editor view
///   - GET  /Logistics/GetBOMsPage         → sayfali JSON
///   - GET  /Logistics/GetBOM              → materialCode+configCode lookup
///   - GET  /Logistics/GetBOMById/{id}     → id lookup
///   - POST /Logistics/SaveBOM             → JSON upsert
///   - POST /Logistics/DeleteBOMJson       → JSON soft delete
///
/// LogisticsController'da kalan (cross-aggregate bagimlilik):
///   - GetMaterialCost (PriceListService + CurrencyService + CardGroupRepo)
///   - StockLookup, CombinationLookup (Item + ProductConfiguration referansli)
/// </summary>
[Authorize]
[Route("Logistics/[action]")]
public sealed class BomController : Controller
{
    private const int BomPageSize = 50;

    private readonly ILogisticsConfigurationService _logisticsConfigurationService;
    private readonly IWidgetService _widgetService;

    public BomController(
        ILogisticsConfigurationService logisticsConfigurationService,
        IWidgetService widgetService)
    {
        _logisticsConfigurationService = logisticsConfigurationService;
        _widgetService = widgetService;
    }

    // ── Board config + helpers ────────────────────────────────────────

    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var allBoms = await _logisticsConfigurationService.GetBOMsAsync(ct);
        var totalCount = allBoms.Count;
        var pageItems = allBoms.Take(BomPageSize).ToList();
        var masterWidgets = await BuildMasterWidgetsAsync(ct);
        var entities = BuildEntities(pageItems);

        return new
        {
            boardKey = "logistics-boms",
            title = "Urun Agaci (Receteler)",
            subtitle = totalCount.ToString("N0") + " recete",
            icon = "GitBranch",
            iconColor = "emerald",
            searchPlaceholder = "Recete ara... (mamul kodu, adi)",
            emptyText = "Henuz recete tanimlanmamis",
            apiUrl = "/Logistics/GetBOMsPage",
            totalCount,
            pageSize = BomPageSize,
            actions = new[]
            {
                new { id = "new", label = "Yeni Recete", icon = "Plus", variant = "primary", url = "/Logistics/BOMEdit" }
            },
            masterWidgets,
            entities,
        };
    }

    private async Task<List<object>> BuildMasterWidgetsAsync(CancellationToken ct)
    {
        var masterWidgets = new List<object>();
        var schema = await _widgetService.GetFormSchemaByCodeAsync("PRODUCT_TREES", ct);
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
        return masterWidgets;
    }

    private static List<object> BuildEntities(IEnumerable<BOMDto> boms) =>
        boms.Select(b => (object)new
        {
            id          = b.Id,
            title       = string.IsNullOrEmpty(b.ItemName) ? b.ItemCode : b.ItemName,
            subtitle    = string.IsNullOrWhiteSpace(b.ConfigCode)
                          ? b.ItemCode
                          : $"{b.ItemCode} · {b.ConfigCode}",
            description = string.IsNullOrEmpty(b.Description)
                          ? $"{b.Lines.Count} bilesen"
                          : b.Description,
            imageUrl    = b.ImageData != null && !string.IsNullOrEmpty(b.ImageMimeType)
                          ? $"data:{b.ImageMimeType};base64,{Convert.ToBase64String(b.ImageData)}"
                          : (string?)null,
            statusBadge = string.IsNullOrWhiteSpace(b.ConfigCode)
                          ? (object?)null
                          : new { label = b.ConfigCode, color = "violet" },
            widgets     = Array.Empty<object>(),
            primaryAction = new
            {
                label = "Duzenle",
                icon  = "Edit",
                url   = $"/Logistics/BOMEdit?id={b.Id}",
            },
            secondaryAction = new
            {
                label   = "Sil",
                icon    = "Trash2",
                apiUrl  = $"/Logistics/DeleteBOMJson?id={b.Id}",
                confirm = "Bu receteyi silmek istediginizden emin misiniz?",
            },
        }).ToList();

    // ── Endpoints ─────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetBOMsPage(int page = 1, int pageSize = 50, string? search = null, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 10) pageSize = 10;
        if (pageSize > 200) pageSize = 200;

        try
        {
            var allBoms = await _logisticsConfigurationService.GetBOMsAsync(ct);
            IEnumerable<BOMDto> filtered = allBoms;
            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim();
                filtered = allBoms.Where(b =>
                    (b.ItemCode != null && b.ItemCode.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (b.ItemName != null && b.ItemName.Contains(q, StringComparison.OrdinalIgnoreCase)));
            }
            var list = filtered.ToList();
            var totalCount = list.Count;
            var pageItems = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            var entities = BuildEntities(pageItems);
            return Json(new { entities, totalCount, page, pageSize });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> BOMs(string? mat = null, string? cfg = null, CancellationToken ct = default)
    {
        // Backwards-compat: /Logistics/BOMs?mat=X&cfg=Y → /Logistics/BOMEdit?id=N
        if (!string.IsNullOrWhiteSpace(mat))
        {
            var bom = await _logisticsConfigurationService.GetBOMByCodeAsync(mat, cfg, ct);
            if (bom is not null)
                return RedirectToAction(nameof(BOMEdit), new { id = bom.Id });
            return RedirectToAction(nameof(BOMEdit));
        }

        var boardConfig = await BuildBoardConfigAsync(ct);

        var viewModel = new BomsViewModel
        {
            Boms = [],
            ListState = new BomListStateViewModel
            {
                GridKey = "logistics-boms",
                Page = 1,
                PageSize = BomPageSize,
                TotalCount = 0,
                TotalPages = 0,
                ItemLabel = "recete",
                PageSizeOptions = new[] { 25, 50, 100, 200 }
                    .Select(s => new SelectListItem(s.ToString(), s.ToString(), s == BomPageSize))
                    .ToList(),
            },
            AvailableColumns = [],
            VisibleColumns = [],
            BoardConfig = boardConfig,
        };
        return View(viewModel);
    }

    [HttpGet]
    public IActionResult BOMEdit(int? id, CancellationToken ct)
    {
        ViewData["BOMEditId"] = id ?? 0;
        ViewBag.Title = "Ürün Ağacı / Reçete Düzenle";
        ViewBag.BOMId = id is > 0 ? id.Value.ToString() : string.Empty;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetBOM(string materialCode, string? configCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            return BadRequest(new { found = false });

        var tree = await _logisticsConfigurationService.GetBOMByCodeAsync(materialCode, configCode, ct);
        if (tree is null)
            return Ok(new { found = false });

        return Ok(BuildBomResponse(tree));
    }

    [HttpGet]
    public async Task<IActionResult> GetBOMById(int id, CancellationToken ct)
    {
        if (id <= 0) return BadRequest(new { found = false });

        var tree = await _logisticsConfigurationService.GetBOMByIdAsync(id, ct);
        if (tree is null) return Ok(new { found = false });

        return Ok(BuildBomResponse(tree));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteBOMJson(int id, CancellationToken ct)
    {
        try
        {
            await _logisticsConfigurationService.DeleteBOMAsync(id, ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveBOM([FromBody] SaveBOMRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { success = false, message = "Geçersiz istek." });

        try
        {
            var id = await _logisticsConfigurationService.SaveBOMAsync(request, ct);
            return Ok(new { success = true, id });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static object BuildBomResponse(BOMWithNames tree) => new
    {
        found         = true,
        id            = tree.Id,
        itemId        = tree.ItemId,
        itemCode      = tree.ItemCode,
        itemName      = tree.ItemName,
        configId      = tree.ConfigId,
        configCode    = tree.ConfigCode,
        description   = tree.Description,
        imageBase64   = tree.ImageData   != null ? Convert.ToBase64String(tree.ImageData) : null,
        imageMimeType = tree.ImageMimeType,
        imageFitMode  = tree.ImageFitMode ?? "square",
        imageRotation = tree.ImageRotation,
        lines         = tree.Lines.Select(l => new
        {
            itemId                = l.ItemId,
            componentMaterialCode = l.ComponentMaterialCode,
            componentMaterialName = l.ComponentMaterialName,
            configId              = l.ConfigId,
            componentConfigCode   = l.ComponentConfigCode,
            quantity              = l.Quantity,
            scrapRatio            = l.ScrapRatio,
        }),
    };
}

