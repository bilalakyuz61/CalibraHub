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
    private readonly IInventoryCountRepository _inventoryCountRepo;
    private readonly ILogisticsConfigurationService _logisticsService;
    private readonly IArgeProjectService _argeService;
    private readonly IFieldSettingRepository _fieldSettings;
    private readonly ICompanyParameterService _companyParams;
    private readonly IDocumentTypeRepository _documentTypeRepo;
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
        IInventoryCountRepository inventoryCountRepo,
        ILogisticsConfigurationService logisticsService,
        IArgeProjectService argeService,
        IFieldSettingRepository fieldSettings,
        ICompanyParameterService companyParams,
        IDocumentTypeRepository documentTypeRepo,
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions dbOptions)
    {
        _stockDocRepo = stockDocRepo;
        _inventoryCountRepo = inventoryCountRepo;
        _logisticsService = logisticsService;
        _argeService = argeService;
        _fieldSettings = fieldSettings;
        _companyParams = companyParams;
        _documentTypeRepo = documentTypeRepo;
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
        var bindings = await GetLineGuideBindingsAsync(FormCodes.TransferLines, ct);
        var gridConfig = BuildLineGridConfig("TRANSFER", bindings);
        var vm = new StockDocEditViewModel
        {
            DocId   = id,
            DocType = "TRANSFER",
            LineGridConfigJson = JsonSerializer.Serialize(gridConfig, BoardJsonOpts),
        };
        return View("StockDocEdit", vm);
    }

    /// <summary>
    /// Kalem grid'i rehber (tip-1) bağlantıları: önce formun kendi kodu, kayıt yoksa
    /// SALES_QUOTE_LINES varsayılanından devral — kalem yapıları özdeş (Sales ile aynı fallback).
    /// </summary>
    private async Task<IReadOnlyCollection<FieldGuideBindingDto>> GetLineGuideBindingsAsync(string lineFormCode, CancellationToken ct)
    {
        var bindings = await _fieldSettings.GetGuideBindingsForFormAsync(lineFormCode, ct);
        if (bindings.Count == 0)
            bindings = await _fieldSettings.GetGuideBindingsForFormAsync("SALES_QUOTE_LINES", ct);
        return bindings;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SAYIM
    // ═══════════════════════════════════════════════════════════════════════

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> Inventory(CancellationToken ct)
    {
        var config = await BuildInventoryBoardConfigAsync(ct);
        return View(new WarehouseBoardViewModel { BoardConfig = config });
    }

    [HttpGet("/Warehouse/InventoryBoardConfig")]
    public async Task<IActionResult> InventoryBoardConfig(CancellationToken ct)
        => Json(await BuildInventoryBoardConfigAsync(ct));

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> InventoryEdit(int? id, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        var bindings = await GetLineGuideBindingsAsync("INVENTORY_COUNT_LINES", ct);
        var gridConfig = BuildInventoryLineGridConfig(bindings);
        var vm = new InventoryEditViewModel
        {
            DocId              = id,
            LineGridConfigJson = JsonSerializer.Serialize(gridConfig, BoardJsonOpts),
        };
        return View(vm);
    }

    // Sayım deposundaki beklenen stok miktarlarını döndürür (stok hareketleri üzerinden)
    [HttpGet]
    public async Task<IActionResult> GetLocationStockSnapshotJson(int locationId, string? date, CancellationToken ct)
    {
        if (locationId <= 0) return Json(Array.Empty<object>());
        var upToDate   = DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture,
                             System.Globalization.DateTimeStyles.None, out var d)
                         ? d.Date : DateTime.Today;
        var companyId  = _connectionFactory.ResolveCurrentCompanyId();

        // Stok etkisi kapalı (STOCK_EFFECT_{code}=false) belge türlerini bakiye dışı bırak
        var disabledTypeIds = await CalibraHub.Application.Services.StockEffectHelper
            .GetDisabledDocTypeIdsAsync(_companyParams, _documentTypeRepo, ct);
        var seFilter = disabledTypeIds.Count == 0
            ? ""
            : $" AND (d.DocumentTypeId IS NULL OR d.DocumentTypeId NOT IN ({string.Join(",", disabledTypeIds.Select((_, i) => $"@sef{i}"))}))";

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT
                l.ItemId,
                i.Code             AS material_code,
                i.Name             AS material_name,
                l.UnitId,
                u.Code             AS unit_code,
                l.CombinationId,
                cfg.RecordCode     AS combination_code,
                SUM(
                    CASE
                        WHEN l.MovementType = 2 AND l.LocationId     = @LocId THEN  l.Quantity
                        WHEN l.MovementType = 1 AND l.FromLocationId = @LocId THEN -l.Quantity
                        WHEN l.MovementType = 3 AND l.LocationId     = @LocId THEN  l.Quantity
                        WHEN l.MovementType = 3 AND l.FromLocationId = @LocId THEN -l.Quantity
                        WHEN l.MovementType = 4 AND l.LocationId     = @LocId THEN  l.Quantity
                        WHEN l.MovementType = 4 AND l.FromLocationId = @LocId THEN -l.Quantity
                        ELSE 0
                    END
                ) AS expected_qty
            FROM [{_schema}].[Document] d
            JOIN [{_schema}].[DocumentLine] l ON l.DocumentId = d.id
            LEFT JOIN [{_schema}].[Items]             i   ON i.Id   = l.ItemId
            LEFT JOIN [{_schema}].[Unit]              u   ON u.Id   = l.UnitId
            LEFT JOIN [{_schema}].[ItemConfiguration] cfg ON cfg.Id = l.CombinationId
            WHERE d.CompanyId = @CompanyId
              AND d.IsActive  = 1
              AND CONVERT(DATE, d.DocumentDate) <= @Date
              AND l.MovementType IN (1,2,3,4)
              AND (l.FromLocationId = @LocId OR l.LocationId = @LocId){seFilter}
            GROUP BY l.ItemId, i.Code, i.Name, l.UnitId, u.Code, l.CombinationId, cfg.RecordCode
            HAVING SUM(
                CASE
                    WHEN l.MovementType = 2 AND l.LocationId     = @LocId THEN  l.Quantity
                    WHEN l.MovementType = 1 AND l.FromLocationId = @LocId THEN -l.Quantity
                    WHEN l.MovementType = 3 AND l.LocationId     = @LocId THEN  l.Quantity
                    WHEN l.MovementType = 3 AND l.FromLocationId = @LocId THEN -l.Quantity
                    WHEN l.MovementType = 4 AND l.LocationId     = @LocId THEN  l.Quantity
                    WHEN l.MovementType = 4 AND l.FromLocationId = @LocId THEN -l.Quantity
                    ELSE 0
                END
            ) > 0
            ORDER BY i.Code;
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@LocId",     locationId);
        cmd.Parameters.AddWithValue("@Date",      upToDate);
        for (var i = 0; i < disabledTypeIds.Count; i++)
            cmd.Parameters.AddWithValue($"@sef{i}", disabledTypeIds[i]);

        var result = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new
            {
                itemId          = r.GetInt32(0),
                materialCode    = r.IsDBNull(1) ? null : r.GetString(1),
                materialName    = r.IsDBNull(2) ? null : r.GetString(2),
                unitId          = r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                unitCode        = r.IsDBNull(4) ? null : r.GetString(4),
                combinationId   = r.IsDBNull(5) ? (int?)null : r.GetInt32(5),
                combinationCode = r.IsDBNull(6) ? null : r.GetString(6),
                expectedQty     = r.GetDecimal(7),
            });
        }
        return Json(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> SaveInventoryJson([FromBody] SaveStockDocRequest? request, CancellationToken ct)
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
            Console.Error.WriteLine($"[SaveInventoryJson] HATA: {ex}");
            return Json(new { success = false, message = "İşlem sırasında bir hata oluştu: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> DeleteInventoryJson(int id, CancellationToken ct)
    {
        try
        {
            await _stockDocRepo.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>
    /// Yansıt (2026-07-02) — taslak sayım satırlarını güncel bakiyeyle karşılaştırır, farkları
    /// DocumentLine'a (Adjust) atomik yazar. Idempotent: aynı sayım ikinci kez yansıtılamaz.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> ApplyInventoryJson(int id, CancellationToken ct)
    {
        try
        {
            var writtenCount = await _inventoryCountRepo.ApplyAsync(id, ct);
            return Json(new { ok = true, writtenCount });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
        catch
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>Sayım bağlantısız bakiye sıfırlama — depodaki TÜM canlı bakiyeleri sıfırlayan Adjust satırları yazar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> ZeroLocationBalancesJson(int id, CancellationToken ct)
    {
        try
        {
            var writtenCount = await _inventoryCountRepo.ZeroLocationBalancesAsync(id, ct);
            return Json(new { ok = true, writtenCount });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
        catch
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>Sayılmayan stokların sıfırlanması — sayım kalemlerinde yer almayan bakiyeleri sıfırlar.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.InventoryCount)]
    public async Task<IActionResult> ZeroUncountedJson(int id, CancellationToken ct)
    {
        try
        {
            var writtenCount = await _inventoryCountRepo.ZeroUncountedAsync(id, ct);
            return Json(new { ok = true, writtenCount });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
        catch
        {
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AMBAR GİRİ�? / ÇIKI�?
    // ═══════════════════════════════════════════════════════════════════════

    // Legacy birleşik "Ambar Giriş / Çıkış" ekranı KALDIRILDI (2026-07-09): her ekran kendi
    // tek "+" butonunu taşır (Giriş → Giriş Belgesi, Çıkış → Çıkış Belgesi). Menü zaten ayrık
    // (/Warehouse/StockIn + /Warehouse/StockOut). Bu endpoint yalnız eski tab/bookmark/dashboard
    // linkleri için korunur ve ayrık Giriş ekranına yönlendirir → iki-buton ekranı hiçbir yerden çıkmaz.
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.StockIn)]
    public IActionResult StockEntry() => Redirect("/Warehouse/StockIn");

    // Eski birleşik board'un refreshUrl'i buraya gelir; hâlâ açık kalmış eski bir tab refresh
    // olunca da tek-butonlu (Giriş) config alsın diye "in" döner (birleşik/null değil).
    [HttpGet("/Warehouse/StockEntryBoardConfig")]
    public async Task<IActionResult> StockEntryBoardConfig(CancellationToken ct)
        => Json(await BuildStockEntryBoardConfigAsync("in", ct));

    // 2026-07-06: Ambar Giriş / Çıkış menüde ayrıldı — her tip kendi listesini açar.
    // StockEntry (birleşik) eski link/dashboard'lar için backward-compat korunur.
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.StockIn)]
    public async Task<IActionResult> StockIn(CancellationToken ct)
    {
        ViewData["Title"] = "Ambar Giriş";
        var config = await BuildStockEntryBoardConfigAsync("in", ct);
        return View("StockEntry", new WarehouseBoardViewModel { BoardConfig = config });
    }

    [HttpGet("/Warehouse/StockInBoardConfig")]
    public async Task<IActionResult> StockInBoardConfig(CancellationToken ct)
        => Json(await BuildStockEntryBoardConfigAsync("in", ct));

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.StockOut)]
    public async Task<IActionResult> StockOut(CancellationToken ct)
    {
        ViewData["Title"] = "Ambar Çıkış";
        var config = await BuildStockEntryBoardConfigAsync("out", ct);
        return View("StockEntry", new WarehouseBoardViewModel { BoardConfig = config });
    }

    [HttpGet("/Warehouse/StockOutBoardConfig")]
    public async Task<IActionResult> StockOutBoardConfig(CancellationToken ct)
        => Json(await BuildStockEntryBoardConfigAsync("out", ct));

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

        var lineFormCode = docType == "STOCK_OUT" ? FormCodes.StockOutLines : FormCodes.StockInLines;
        var bindings = await GetLineGuideBindingsAsync(lineFormCode, ct);
        var gridConfig = BuildLineGridConfig(docType, bindings);
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
        catch (CalibraHub.Domain.Exceptions.NegativeBalanceException nbex)
        {
            return Json(new { success = false, message = nbex.Message });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>Satış siparişi teslimatı — açık kalemler için fiziksel çıkış yazar + rezervasyonu serbest bırakır (Faz 2).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeliverSalesOrderJson(int orderId, CancellationToken ct)
    {
        if (orderId <= 0) return Json(new { success = false, message = "Sipariş bulunamadı." });
        try
        {
            var (id, docNo) = await _stockDocRepo.DeliverSalesOrderAsync(orderId, CurrentUserId(), ct);
            return Json(new { success = true, id, docNo });
        }
        catch (CalibraHub.Domain.Exceptions.NegativeBalanceException nbex)
        {
            return Json(new { success = false, message = nbex.Message });
        }
        catch (InvalidOperationException ioex)
        {
            return Json(new { success = false, message = ioex.Message });
        }
        catch (Exception)
        {
            return Json(new { success = false, message = "Teslimat sırasında bir hata oluştu." });
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
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetLocationsJson(CancellationToken ct)
    {
        var locs    = await _logisticsService.GetLocationsAsync(ct);
        var active  = locs.Where(l => l.IsActive).ToList();
        // Alt lokasyonu olan (parent) lokasyonlar seçilemez — yalnızca yaprak (leaf) lokasyonlar döner
        var parentIds = active.Where(l => l.ParentId.HasValue)
                              .Select(l => l.ParentId!.Value)
                              .ToHashSet();
        return Json(active
            .Where(l => !parentIds.Contains(l.Id))
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

    /// <summary>
    /// type: "in" → yalnız Ambar Giriş, "out" → yalnız Ambar Çıkış, null → birleşik liste
    /// (eski /Warehouse/StockEntry linki için backward-compat). Her mod kendi boardKey'ini
    /// taşır — widget/filtre tercihleri localStorage'da ayrı saklanır.
    /// </summary>
    private async Task<object> BuildStockEntryBoardConfigAsync(string? type, CancellationToken ct)
    {
        var isIn  = string.Equals(type, "in",  StringComparison.OrdinalIgnoreCase);
        var isOut = string.Equals(type, "out", StringComparison.OrdinalIgnoreCase);
        string[] docTypes = isIn ? ["STOCK_IN"] : isOut ? ["STOCK_OUT"] : ["STOCK_IN", "STOCK_OUT"];
        var docs = await _stockDocRepo.GetByTypesAsync(docTypes, ct);
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

        var actions = isIn
            ? new object[] { new { id = "new-in",  label = "Giriş Belgesi", icon = "Plus", variant = "primary",
                                   url = "/Warehouse/StockEntryEdit?type=in" } }
            : isOut
            ? new object[] { new { id = "new-out", label = "Çıkış Belgesi", icon = "Plus", variant = "primary",
                                   url = "/Warehouse/StockEntryEdit?type=out" } }
            : new object[]
            {
                new { id = "new-in",  label = "Giriş Belgesi",  icon = "Plus", variant = "primary",
                      url = "/Warehouse/StockEntryEdit?type=in" },
                new { id = "new-out", label = "Çıkış Belgesi",  icon = "Plus", variant = "secondary",
                      url = "/Warehouse/StockEntryEdit?type=out" },
            };

        return new
        {
            boardKey          = isIn ? "warehouse-stock-in" : isOut ? "warehouse-stock-out" : "warehouse-stock-entry",
            title             = isIn ? "Ambar Giriş" : isOut ? "Ambar Çıkış" : "Ambar Giriş / Çıkış",
            subtitle          = $"{entities.Count} belge",
            icon              = isIn ? "PackagePlus" : isOut ? "PackageMinus" : "Warehouse",
            iconColor         = isOut ? "rose" : "emerald",
            refreshUrl        = isIn ? "/Warehouse/StockInBoardConfig"
                              : isOut ? "/Warehouse/StockOutBoardConfig"
                              : "/Warehouse/StockEntryBoardConfig",
            searchPlaceholder = "Belge no ara…",
            emptyText         = isIn ? "Henüz giriş belgesi oluşturulmamış"
                              : isOut ? "Henüz çıkış belgesi oluşturulmamış"
                              : "Henüz ambar belgesi oluşturulmamış",
            actions,
            masterWidgets,
            entities,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Line grid config — CalibraLineItemsGrid için server-side JSON
    // ═══════════════════════════════════════════════════════════════════════

    private static object BuildLineGridConfig(string docType, IReadOnlyCollection<FieldGuideBindingDto>? bindings = null)
    {
        var isTransfer = docType == "TRANSFER";
        var bindingMap = (bindings ?? [])
            .ToDictionary(b => b.FieldKey, b => b, StringComparer.OrdinalIgnoreCase);
        bindingMap.TryGetValue("materialCode", out var matBinding);
        var lineFormCode = docType switch
        {
            "TRANSFER"  => FormCodes.TransferLines,
            "STOCK_OUT" => FormCodes.StockOutLines,
            _           => FormCodes.StockInLines,
        };

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
                guideCode      = matBinding?.GuideCode,
                filterJson     = matBinding?.FilterJson,
                formCode       = lineFormCode,
                formatJson     = matBinding?.FormatJson,
                lookupUrl      = matBinding == null ? "/Warehouse/GetMaterialsJson" : (string?)null,
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
            // Satır bazlı "Notlar" sütunu kaldırıldı (2026-07-07): belge notu üst
            // bilgideki Notlar alanında tutulur. Kalem bazlı not gerekirse Admin →
            // Widget Tanımları ile bu form code'a ("depo_giris/cikis/transfer_lines")
            // alan tanımlanır — hardcoded kolon eklenmez.
        };

        return new
        {
            schemaVersion = "v1",
            columns = locationCols.Concat(baseCols).ToArray(),
        };
    }

    private async Task<object> BuildInventoryBoardConfigAsync(CancellationToken ct)
    {
        var docs = await _stockDocRepo.GetByTypeAsync("INVENTORY_COUNT", ct);
        var tr   = CultureInfo.GetCultureInfo("tr-TR");
        var masterWidgets = new List<object>
        {
            SmartBoardFilterHelpers.MakeStdWidget("w_depo",  "Depo",  "text"),
            SmartBoardFilterHelpers.MakeStdWidget("w_kalem", "Kalem", "numeric"),
        };

        var entities = docs.Select(d => new
        {
            id          = d.Id,
            title       = d.DocNo,
            subtitle    = d.DocDate.ToString("dd.MM.yyyy", tr),
            description = d.FromLocationName ?? "",
            imageUrl    = (string?)null,
            statusBadge = (object?)null,
            widgets = new object[]
            {
                new { id = "w_depo",  type = "data", dataType = "text",    label = "Depo",
                      value = d.FromLocationName ?? "-", detail = (string?)null, color = "violet" },
                new { id = "w_kalem", type = "data", dataType = "numeric", label = "Kalem",
                      value = d.LineCount.ToString(CultureInfo.InvariantCulture), detail = "kalem", color = "slate" },
            },
            primaryAction = new
            {
                label = "Düzenle", icon = "Edit", color = "amber",
                url   = $"/Warehouse/InventoryEdit?id={d.Id}", hideButton = true,
            },
            secondaryAction = new
            {
                label     = "Sil", icon = "Trash2",
                apiUrl    = $"/Warehouse/DeleteInventoryJson?id={d.Id}",
                apiMethod = "POST",
                confirm   = $"Bu sayım belgesini silmek istediğinizden emin misiniz? ({d.DocNo})",
            },
        }).ToList();

        return new
        {
            boardKey          = "warehouse-inventory",
            title             = "Stok Sayımı",
            subtitle          = $"{entities.Count} belge",
            icon              = "ClipboardCheck",
            iconColor         = "violet",
            refreshUrl        = "/Warehouse/InventoryBoardConfig",
            searchPlaceholder = "Belge no, depo ara…",
            emptyText         = "Henüz sayım belgesi oluşturulmamış",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Sayım", icon = "Plus", variant = "primary",
                      url = "/Warehouse/InventoryEdit" },
            },
            masterWidgets,
            entities,
        };
    }

    private static object BuildInventoryLineGridConfig(IReadOnlyCollection<FieldGuideBindingDto>? bindings = null)
    {
        var bindingMap = (bindings ?? [])
            .ToDictionary(b => b.FieldKey, b => b, StringComparer.OrdinalIgnoreCase);
        bindingMap.TryGetValue("materialCode", out var matBinding);

        return new
        {
            schemaVersion = "v1",
            // Ondalık: lineFormCode yok — sayım ailesinin kök kodu açıkça verilmezse
            // grid SALES_QUOTE_LINES varsayılanına düşer (yanlış aile).
            decimalFormCode = FormCodes.InventoryCount,
            columns = new object[]
            {
            new
            {
                key            = "materialCode",
                label          = "Malzeme Kodu",
                type           = "text-lookup",
                guideCode      = matBinding?.GuideCode,
                filterJson     = matBinding?.FilterJson,
                formCode       = "INVENTORY_COUNT_LINES",
                formatJson     = matBinding?.FormatJson,
                lookupUrl      = matBinding == null ? "/Warehouse/GetMaterialsJson" : (string?)null,
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
                required = matBinding?.IsRequired ?? false,
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
                label     = "Sayılan Miktar",
                type      = "number",
                width     = 130,
                precision = 2,
                min       = 0,
                align     = "right",
                icon      = "Sigma",
            },
            new
            {
                key             = "fromLocationId",
                label           = "Lokasyon",
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
                key   = "notes",
                label = "Notlar",
                type  = "text",
                width = 180,
                align = "left",
                icon  = "MessageSquare",
            },
        },
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
