using CalibraHub.Application.Constants;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Web.Helpers;
using CalibraHub.Web.Models.Warehouse;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class WarehouseController : Controller
{
    private readonly IStockDocRepository _stockDocRepo;
    private readonly ILogisticsConfigurationService _logisticsService;
    private readonly IArgeProjectService _argeService;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    private static readonly JsonSerializerOptions BoardJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public WarehouseController(
        IStockDocRepository stockDocRepo,
        ILogisticsConfigurationService logisticsService,
        IArgeProjectService argeService,
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions dbOptions)
    {
        _stockDocRepo = stockDocRepo;
        _logisticsService = logisticsService;
        _argeService = argeService;
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(dbOptions.Schema) ? "dbo" : dbOptions.Schema.Trim();
    }

    private string CurrentUser() => User.FindFirstValue(ClaimTypes.Name) ?? "system";
    private int? CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    // ═══════════════════════════════════════════════════════════════════════
    // TRANSFER
    // ═══════════════════════════════════════════════════════════════════════

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.Transfer)]
    public async Task<IActionResult> Transfer(CancellationToken ct)
    {
        var config = await BuildTransferBoardConfigAsync(ct);
        return View(new WarehouseBoardViewModel { BoardConfig = config });
    }

    [HttpGet("/Warehouse/TransferBoardConfig")]
    public async Task<IActionResult> TransferBoardConfig(CancellationToken ct)
        => Json(await BuildTransferBoardConfigAsync(ct));

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.Transfer)]
    public async Task<IActionResult> TransferEdit(int? id, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        var gridConfig = BuildLineGridConfig("TRANSFER");
        var vm = new StockDocEditViewModel
        {
            DocId   = id,
            DocType = "TRANSFER",
            LineGridConfigJson = JsonSerializer.Serialize(gridConfig, BoardJsonOpts),
        };
        return View("StockDocEdit", vm);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SAYIM — 2026-05-22 iskeleti
    // ═══════════════════════════════════════════════════════════════════════

    [HttpGet("/Warehouse/Inventory")]
    public IActionResult Inventory()
        => View("_ComingSoon", new CalibraHub.Web.Models.ComingSoonViewModel
        {
            Title = "Sayım",
            Description = "Depolarda fiziksel stok sayim planlama, terminal ile sayim girisi ve fark raporu yakinda.",
        });

    // ═══════════════════════════════════════════════════════════════════════
    // AMBAR GİRİŞ / ÇIKIŞ
    // ═══════════════════════════════════════════════════════════════════════

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.StockIn)]
    public async Task<IActionResult> StockEntry(CancellationToken ct)
    {
        var config = await BuildStockEntryBoardConfigAsync(ct);
        return View(new WarehouseBoardViewModel { BoardConfig = config });
    }

    [HttpGet("/Warehouse/StockEntryBoardConfig")]
    public async Task<IActionResult> StockEntryBoardConfig(CancellationToken ct)
        => Json(await BuildStockEntryBoardConfigAsync(ct));

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.StockIn)]
    public async Task<IActionResult> StockEntryEdit(int? id, string? type, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        var docType = string.Equals(type, "out", StringComparison.OrdinalIgnoreCase)
            ? "STOCK_OUT" : "STOCK_IN";

        // Mevcut belge varsa gerçek tipini oku
        if (id.HasValue && id.Value > 0)
        {
            var existing = await _stockDocRepo.GetByIdAsync(id.Value, ct);
            if (existing != null) docType = existing.DocType;
        }

        var gridConfig = BuildLineGridConfig(docType);
        var vm = new StockDocEditViewModel
        {
            DocId   = id,
            DocType = docType,
            LineGridConfigJson = JsonSerializer.Serialize(gridConfig, BoardJsonOpts),
        };
        return View("StockDocEdit", vm);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SHARED API
    // ═══════════════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> GetDocJson(int id, CancellationToken ct)
    {
        var doc = await _stockDocRepo.GetByIdAsync(id, ct);
        if (doc == null) return NotFound();
        var lines = await _stockDocRepo.GetLinesAsync(id, ct);
        return Json(new { doc, lines });
    }

    [HttpPost]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.StockIn)]
    public async Task<IActionResult> SaveDocJson([FromBody] SaveStockDocRequest? request, CancellationToken ct)
    {
        if (request is null)
            return Json(new { success = false, message = "Geçersiz istek." });
        try
        {
            var (id, docNo) = await _stockDocRepo.SaveAsync(request, CurrentUserId(), ct);
            return Json(new { success = true, id, docNo });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.StockIn)]
    public async Task<IActionResult> DeleteDocJson(int id, CancellationToken ct)
    {
        try
        {
            await _stockDocRepo.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetLocationsJson(CancellationToken ct)
    {
        var locs = await _logisticsService.GetLocationsAsync(ct);
        return Json(locs
            .Where(l => l.IsActive)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.LocationName)
            .Select(l => new { l.Id, l.LocationCode, l.LocationName }));
    }

    // AR-GE/ÜR-GE proje listesi — ambar çıkış fişinde "Proje" seçici için (sarf malzeme takibi).
    [HttpGet]
    public async Task<IActionResult> GetArgeProjectsJson(CancellationToken ct)
    {
        var projects = await _argeService.ListAsync(null, null, ct);
        return Json(projects.Select(p => new
        {
            id = p.DocumentId,
            name = p.Name,
            projectType = p.ProjectType,
            label = (p.ProjectType == 1 ? "ÜR-GE" : "AR-GE") + " · " + p.DocumentNumber + " · " + p.Name,
        }));
    }

    [HttpGet]
    public async Task<IActionResult> GetMaterialsJson(CancellationToken ct)
    {
        var snapshot = await _logisticsService.GetSnapshotAsync(ct);
        return Json(snapshot.Items
            .Where(x => x.IsActive)
            .Select(x => new
            {
                Id = x.Id,
                MaterialCode = x.Code,
                MaterialName = x.Name,
                x.UnitId,
                TrackCombinations = x.Combinations,
            })
            .OrderBy(x => x.MaterialCode));
    }

    [HttpGet]
    public async Task<IActionResult> GetMaterialUnitsJson(string materialCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            return Json(Array.Empty<object>());

        var allUnits = await _logisticsService.GetUnitsAsync(ct);
        var unitById = allUnits.Where(u => u.IsActive).ToDictionary(u => u.Id);

        var snapshot = await _logisticsService.GetSnapshotAsync(ct);
        var item = snapshot.Items.FirstOrDefault(x =>
            string.Equals(x.Code, materialCode, StringComparison.OrdinalIgnoreCase));
        if (item == null) return Json(Array.Empty<object>());

        var seen = new HashSet<int>();
        var result = new List<object>();

        void AddUnit(int? id)
        {
            if (!id.HasValue || !seen.Add(id.Value)) return;
            if (!unitById.TryGetValue(id.Value, out var u)) return;
            result.Add(new { id = u.Id, code = u.Code, name = u.Name });
        }

        AddUnit(item.UnitId);
        var conversions = await _logisticsService.GetItemUnitsAsync(item.Id, ct);
        foreach (var cv in conversions) AddUnit(cv.UnitId);

        return Json(result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Board config builders
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<object> BuildTransferBoardConfigAsync(CancellationToken ct)
    {
        var docs = await _stockDocRepo.GetByTypeAsync("TRANSFER", ct);
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var masterWidgets = new List<object>
        {
            SmartBoardFilterHelpers.MakeStdWidget("w_depo",  "Depo",  "text"),
            SmartBoardFilterHelpers.MakeStdWidget("w_kalem", "Kalem", "numeric"),
        };

        var entities = docs.Select(d => new
        {
            id       = d.Id,
            title    = d.DocNo,
            subtitle = d.DocDate.ToString("dd.MM.yyyy", tr),
            description = BuildTransferDesc(d),
            imageUrl    = (string?)null,
            statusBadge = (object?)null,
            widgets = new object[]
            {
                new { id = "w_depo",  type = "data", dataType = "text", label = "Depo",
                      value = "Çoklu Depo", detail = (string?)null, color = "indigo" },
                new { id = "w_kalem", type = "data", dataType = "numeric", label = "Kalem",
                      value = d.LineCount.ToString(CultureInfo.InvariantCulture), detail = "kalem", color = "slate" },
            },
            primaryAction = new
            {
                label = "Düzenle", icon = "Edit", color = "amber",
                url = $"/Warehouse/TransferEdit?id={d.Id}", hideButton = true,
            },
            secondaryAction = new
            {
                label = "Sil", icon = "Trash2",
                apiUrl = $"/Warehouse/DeleteDocJson?id={d.Id}",
                apiMethod = "POST",
                confirm = $"Bu transfer belgesini silmek istediğinizden emin misiniz? ({d.DocNo})",
            },
        }).ToList();

        return new
        {
            boardKey          = "warehouse-transfer",
            title             = "Transfer Belgesi",
            subtitle          = $"{entities.Count} belge",
            icon              = "ArrowLeftRight",
            iconColor         = "indigo",
            refreshUrl        = "/Warehouse/TransferBoardConfig",
            searchPlaceholder = "Belge no, depo ara…",
            emptyText         = "Henüz transfer belgesi oluşturulmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Transfer", icon = "Plus", variant = "primary",
                      url = "/Warehouse/TransferEdit" },
            },
            masterWidgets,
            entities,
        };
    }

    private async Task<object> BuildStockEntryBoardConfigAsync(CancellationToken ct)
    {
        var docs = await _stockDocRepo.GetByTypesAsync(["STOCK_IN", "STOCK_OUT"], ct);
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var typeOptions = SmartBoardFilterHelpers.ToOptionsList(new[] { "Giriş", "Çıkış" });
        var masterWidgets = new List<object>
        {
            SmartBoardFilterHelpers.MakeOptionsWidget("w_type",  "Hareket", typeOptions),
            SmartBoardFilterHelpers.MakeStdWidget   ("w_loc",   "Depo",    "text"),
            SmartBoardFilterHelpers.MakeStdWidget   ("w_kalem", "Kalem",   "numeric"),
        };

        var entities = docs.Select(d => new
        {
            id       = d.Id,
            title    = d.DocNo,
            subtitle = d.DocDate.ToString("dd.MM.yyyy", tr),
            description = BuildStockEntryDesc(d),
            imageUrl    = (string?)null,
            statusBadge = (object?)null,
            widgets = new object[]
            {
                new { id = "w_type", type = "data", dataType = "options", label = "Hareket",
                      value = d.DocType == "STOCK_IN" ? "Giriş" : "Çıkış",
                      detail = (string?)null,
                      color = d.DocType == "STOCK_IN" ? "emerald" : "rose" },
                new { id = "w_loc", type = "data", dataType = "text", label = "Depo",
                      value = (d.DocType == "STOCK_IN" ? d.ToLocationName : d.FromLocationName) ?? "-",
                      detail = (string?)null, color = "indigo" },
                new { id = "w_kalem", type = "data", dataType = "numeric", label = "Kalem",
                      value = d.LineCount.ToString(CultureInfo.InvariantCulture), detail = "kalem", color = "slate" },
            },
            primaryAction = new
            {
                label = "Düzenle", icon = "Edit", color = "amber",
                url = $"/Warehouse/StockEntryEdit?id={d.Id}", hideButton = true,
            },
            secondaryAction = new
            {
                label = "Sil", icon = "Trash2",
                apiUrl = $"/Warehouse/DeleteDocJson?id={d.Id}",
                apiMethod = "POST",
                confirm = $"Bu belgeyi silmek istediğinizden emin misiniz? ({d.DocNo})",
            },
        }).ToList();

        return new
        {
            boardKey          = "warehouse-stock-entry",
            title             = "Ambar Giriş / Çıkış",
            subtitle          = $"{entities.Count} belge",
            icon              = "Warehouse",
            iconColor         = "emerald",
            refreshUrl        = "/Warehouse/StockEntryBoardConfig",
            searchPlaceholder = "Belge no ara…",
            emptyText         = "Henüz ambar belgesi oluşturulmamış",
            actions = new object[]
            {
                new { id = "new-in",  label = "Giriş Belgesi",  icon = "Plus", variant = "primary",
                      url = "/Warehouse/StockEntryEdit?type=in" },
                new { id = "new-out", label = "Çıkış Belgesi",  icon = "Plus", variant = "secondary",
                      url = "/Warehouse/StockEntryEdit?type=out" },
            },
            masterWidgets,
            entities,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Line grid config — CalibraLineItemsGrid için server-side JSON
    // ═══════════════════════════════════════════════════════════════════════

    private static object BuildLineGridConfig(string docType)
    {
        var isTransfer = docType == "TRANSFER";

        var locationCols = isTransfer ? new object[]
        {
            new
            {
                key             = "fromLocationId",
                label           = "Kaynak Depo",
                type            = "select",
                optionsUrl      = "/Warehouse/GetLocationsJson",
                optionsValueKey = "id",
                optionsLabelKey = "locationName",
                width           = 160,
                align           = "left",
                icon            = "Warehouse",
            },
            new
            {
                key             = "toLocationId",
                label           = "Hedef Depo",
                type            = "select",
                optionsUrl      = "/Warehouse/GetLocationsJson",
                optionsValueKey = "id",
                optionsLabelKey = "locationName",
                width           = 160,
                align           = "left",
                icon            = "ArrowRight",
            },
        } : Array.Empty<object>();

        var baseCols = new object[]
        {
            new
            {
                key            = "materialCode",
                label          = "Malzeme Kodu",
                type           = "text-lookup",
                lookupUrl      = "/Warehouse/GetMaterialsJson",
                lookupValueKey = "materialCode",
                lookupLabelKey = "materialName",
                lookupFillMap  = new Dictionary<string, string>
                {
                    ["materialName"]      = "materialName",
                    ["stockCardId"]       = "id",
                    ["trackCombinations"] = "trackCombinations",
                    ["unitId"]            = "unitId",
                },
                width    = 200,
                required = true,
                align    = "left",
                icon     = "Hash",
            },
            new
            {
                key      = "materialName",
                label    = "Malzeme Adı",
                type     = "text",
                width    = "flex",
                @readonly = true,
                align    = "left",
                icon     = "FileText",
            },
            new
            {
                key            = "combinationCode",
                label          = "Kombinasyon",
                type           = "combination-lookup",
                width          = 130,
                align          = "center",
                icon           = "CircleDot",
                visibleWhenKey = "trackCombinations",
            },
            new
            {
                key             = "unitId",
                label           = "Birim",
                type            = "select",
                optionsUrl      = "/Warehouse/GetMaterialUnitsJson?materialCode={materialCode}",
                optionsValueKey = "id",
                optionsLabelKey = "name",
                autoSelectFirst = true,
                width           = 80,
                align           = "center",
                icon            = "Ruler",
            },
            new
            {
                key       = "quantity",
                label     = "Miktar",
                type      = "number",
                width     = 100,
                precision = 2,
                min       = 0,
                align     = "right",
                icon      = "Sigma",
            },
            new
            {
                key   = "notes",
                label = "Notlar",
                type  = "text",
                width = 180,
                align = "left",
                icon  = "MessageSquare",
            },
        };

        return new
        {
            schemaVersion = "v1",
            columns = locationCols.Concat(baseCols).ToArray(),
        };
    }

    private static string BuildTransferDesc(StockDocDto d)
        => d.RefNo is { Length: > 0 } r ? $"Ref: {r}" : "";


    private static string BuildStockEntryDesc(StockDocDto d)
    {
        var loc = d.DocType == "STOCK_IN" ? d.ToLocationName : d.FromLocationName;
        return loc ?? "";
    }
}
