using CalibraHub.Application.Constants;
using System.Security.Claims;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Helpers;
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
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.BomEdit)]
public sealed class BomController : Controller
{
    private const int BomPageSize = 50;

    private readonly ILogisticsConfigurationService _logisticsConfigurationService;
    private readonly IWidgetService _widgetService;
    private readonly IPriceListService _priceListService;
    private readonly ICurrencyService _currencyService;

    public BomController(
        ILogisticsConfigurationService logisticsConfigurationService,
        IWidgetService widgetService,
        IPriceListService priceListService,
        ICurrencyService currencyService)
    {
        _logisticsConfigurationService = logisticsConfigurationService;
        _widgetService = widgetService;
        _priceListService = priceListService;
        _currencyService = currencyService;
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
            title = "Ürün Ağacı (Reçeteler)",
            subtitle = totalCount.ToString("N0") + " reçete",
            icon = "GitBranch",
            iconColor = "emerald",
            searchPlaceholder = "Reçete ara... (mamul kodu, adı)",
            emptyText = "Henüz reçete tanımlanmamış",
            apiUrl = "/Logistics/GetBOMsPage",
            totalCount,
            pageSize = BomPageSize,
            itemLabel = "reçete",   // SmartBoard sayfali mod sayac etiketi ("N reçete")
            actions = new[]
            {
                new { id = "new", label = "Yeni Reçete", icon = "Plus", variant = "primary", url = "/Logistics/BOMEdit" }
            },
            masterWidgets,
            entities,
        };
    }

    private async Task<List<object>> BuildMasterWidgetsAsync(CancellationToken ct)
    {
        var schema = await _widgetService.GetFormSchemaByCodeAsync("PRODUCT_TREES", ct);
        return SmartBoardFilterHelpers.BuildAdminFormWidgets(schema);
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
                          ? $"{b.Lines.Count} bileşen"
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
                label = "Düzenle",
                icon  = "Edit",
                url   = $"/Logistics/BOMEdit?id={b.Id}",
            },
            secondaryAction = new
            {
                label   = "Sil",
                icon    = "Trash2",
                apiUrl  = $"/Logistics/DeleteBOMJson?id={b.Id}",
                confirm = "Bu reçeteyi silmek istediğinizden emin misiniz?",
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
        catch (Exception)
        {
            return Json(new { error = "İşlem sırasında bir hata oluştu." });
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
        // Explicit view path — split sonrasi view'lar /Views/Logistics/ altinda kaldi,
        // default resolver /Views/Bom/ arar ve bulamaz (rapor §2.3 split-fixup).
        return View("~/Views/Logistics/BOMs.cshtml", viewModel);
    }

    [HttpGet]
    public IActionResult BOMEdit(int? id, CancellationToken ct)
    {
        ViewData["BOMEditId"] = id ?? 0;
        ViewBag.Title = "Ürün Ağacı / Reçete Düzenle";
        ViewBag.BOMId = id is > 0 ? id.Value.ToString() : string.Empty;
        return View("~/Views/Logistics/BOMEdit.cshtml");
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
            await _logisticsConfigurationService.DeleteBOMAsync(id, CurrentUserId(), ct);
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

        // FluentValidation auto-validation regular Controller'larda ModelState'i set
        // eder ama [ApiController] olmadigi icin otomatik 400 dondurmez. Burada
        // manuel kontrol — frontend'in bekledigi `{success, message}` formati korunur
        // (rapor 2026-05-17 madde 3.11). Birden fazla validation hatasi varsa "•"
        // satir basi ile birlestirilir.
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct()
                .ToArray();
            var msg = errors.Length switch
            {
                0 => "Form bilgilerinde hata var.",
                1 => errors[0],
                _ => "Lutfen asagidaki hatalari duzeltin:\n• " + string.Join("\n• ", errors)
            };
            return BadRequest(new { success = false, message = msg });
        }

        try
        {
            var id = await _logisticsConfigurationService.SaveBOMAsync(request, CurrentUserId(), ct);
            return Ok(new { success = true, id });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>
    /// Multi-level BOM patlatma (rapor 2026-05-17 madde 3.3).
    /// GET /Logistics/ExplodeBOM?itemId=5&amp;quantity=100&amp;configId=
    /// Donus: BOMExplodeResultDto (parent display + duzlestirilmis bilesen listesi
    /// + depth + truncated bayragi). Recete yoksa 404.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExplodeBOM(int itemId, decimal quantity, int? configId, CancellationToken ct)
    {
        try
        {
            var result = await _logisticsConfigurationService.ExplodeBOMAsync(itemId, quantity, configId, ct);
            if (result is null)
                return NotFound(new { success = false, message = $"Mamul bulunamadi veya aktif degil: ItemId={itemId}" });
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Where-used (ters arama): bir bileseni dogrudan kullanan aktif BOM'lar.
    /// GET /Logistics/WhereUsed?itemId=42
    /// 1-seviye (transitive degil). Bos liste = bilesen hicbir recetede gecmiyor.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> WhereUsed(int itemId, CancellationToken ct)
    {
        try
        {
            var rows = await _logisticsConfigurationService.GetWhereUsedAsync(itemId, ct);
            return Ok(rows);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Multi-level BOM standart maliyet hesabi (rapor 2026-05-17 madde 3.8).
    /// ExplodeBOM + PriceListService bilesimi: agaci patlatir, leaf satirlari
    /// secilen fiyat grubundan fiyatlandirir, toplam maliyet doner.
    ///
    /// LogisticsController.GetMaterialCost'tan farki: bu 1-seviye degil multi-level.
    /// Ara mamuller satirlarda gorunur (depth + isLeaf=false) ama TotalCost'a
    /// katkida bulunmaz — alt-recetesindeki leaf'ler zaten sayilmistir.
    ///
    /// GET /Logistics/CalculateBOMCost?itemId=5&amp;quantity=100&amp;priceGroupId=1
    ///                                 &amp;currencyId=1&amp;priceType=m&amp;validOn=2026-05-18
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> CalculateBOMCost(
        int itemId,
        decimal quantity,
        int priceGroupId,
        int currencyId,
        string? priceType = null,
        int? configId = null,
        string? validOn = null,
        CancellationToken ct = default)
    {
        if (priceGroupId <= 0 || currencyId <= 0)
            return BadRequest(new { success = false, message = "Fiyat grubu ve para birimi zorunlu." });

        DateTime priceDate = DateTime.UtcNow.Date;
        if (!string.IsNullOrWhiteSpace(validOn) && DateTime.TryParse(validOn,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal, out var parsedDate))
        {
            priceDate = parsedDate.Date;
        }

        // 1) Multi-level patlatma
        BOMExplodeResultDto? explosion;
        try
        {
            explosion = await _logisticsConfigurationService.ExplodeBOMAsync(itemId, quantity, configId, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        if (explosion is null)
            return NotFound(new { success = false, message = $"Mamul bulunamadi veya aktif degil: ItemId={itemId}" });

        // 2) Leaf satirlarini topla — sadece bunlar fiyatlandirilir
        var leaves = explosion.Lines.Where(l => l.IsLeaf).ToList();
        var keys = leaves
            .Select(l => new PriceEntryKey(l.ItemId, l.ConfigId))
            .ToList();

        // 3) PriceListService toplu fiyat (PriceType: b=Buy / s=Sell / m=Maliyet; default m)
        var pType = string.IsNullOrWhiteSpace(priceType) ? "m" : priceType.Trim();
        var prices = keys.Count == 0
            ? Array.Empty<ExistingPriceRow>()
            : (await _priceListService.GetExistingPricesAsync(
                new GetExistingPricesRequest(priceGroupId, currencyId, pType, priceDate, keys),
                ct)).ToArray();
        var priceByKey = prices.ToDictionary(p => (p.ItemId, p.ConfigId), p => p.Price);

        // 4) Currency display
        var currencies = await _currencyService.GetAllAsync(ct);
        var currency = currencies.FirstOrDefault(c => c.Id == currencyId);

        // 5) Cost line listesi — explosion'daki TUM satirlar (intermediate dahil),
        //    ama LineCost yalnizca leaf icin > 0. Frontend UI'da intermediate'leri
        //    "ara mamul" rozeti ile gosterip toplama katmadigini belirtir.
        decimal totalCost = 0m;
        int missingPriceCount = 0;
        var costLines = explosion.Lines.Select(l =>
        {
            if (!l.IsLeaf)
            {
                return new BOMCostLineDto(
                    ItemId:        l.ItemId,
                    ItemCode:      l.ItemCode,
                    ItemName:      l.ItemName,
                    ConfigId:      l.ConfigId,
                    ConfigCode:    l.ConfigCode,
                    TotalQuantity: l.TotalQuantity,
                    Depth:         l.Depth,
                    IsLeaf:        false,
                    UnitPrice:     0m,
                    LineCost:      0m,
                    HasPrice:      false);
            }
            var unitPrice = priceByKey.TryGetValue((l.ItemId, l.ConfigId), out var p) ? p : 0m;
            var lineCost  = Math.Round(l.TotalQuantity * unitPrice, 2, MidpointRounding.AwayFromZero);
            totalCost += lineCost;
            var hasPrice = unitPrice > 0m;
            if (!hasPrice) missingPriceCount++;
            return new BOMCostLineDto(
                ItemId:        l.ItemId,
                ItemCode:      l.ItemCode,
                ItemName:      l.ItemName,
                ConfigId:      l.ConfigId,
                ConfigCode:    l.ConfigCode,
                TotalQuantity: l.TotalQuantity,
                Depth:         l.Depth,
                IsLeaf:        true,
                UnitPrice:     unitPrice,
                LineCost:      lineCost,
                HasPrice:      hasPrice);
        }).ToList();

        return Ok(new BOMCostResultDto(
            ParentItemId:      explosion.ParentItemId,
            ParentItemCode:    explosion.ParentItemCode,
            ParentItemName:    explosion.ParentItemName,
            ConfigId:          explosion.ConfigId,
            ConfigCode:        explosion.ConfigCode,
            Quantity:          explosion.Quantity,
            PriceGroupId:      priceGroupId,
            CurrencyId:        currencyId,
            CurrencyCode:      currency?.Code,
            CurrencySymbol:    currency?.Symbol,
            PriceType:         pType,
            ValidOn:           priceDate,
            TotalCost:         totalCost,
            MissingPriceCount: missingPriceCount,
            MaxDepth:          explosion.MaxDepth,
            Truncated:         explosion.Truncated,
            Lines:             costLines));
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
        // 2026-05-20: header-level Routing FK (opsiyonel). Frontend BOMEdit
        // sayfasinda Rota dropdown'i bu degerle one-doldurulur.
        routingId     = tree.RoutingId,
        routingCode   = tree.RoutingCode,
        routingName   = tree.RoutingName,
        lines         = tree.Lines.Select(l => new
        {
            itemId                = l.ItemId,
            componentMaterialCode = l.ComponentMaterialCode,
            componentMaterialName = l.ComponentMaterialName,
            configId              = l.ConfigId,
            componentConfigCode   = l.ComponentConfigCode,
            quantity              = l.Quantity,
            scrapRatio            = l.ScrapRatio,
            note                  = l.Note,
        }),
    };

    /// <summary>
    /// 2026-07-05: Excel'den toplu yapıştırma akışı için toplu kod çözümleyici.
    /// POST /Logistics/ResolveItemCodes  body: { codes: ["A","B",...] }
    /// Dönüş: her kod için { code, found, id, resolvedCode, name, hasConfig }.
    /// Tek Items okuması ile N kodu çözer — satır başına StockLookup çağrısı yapılmaz.
    /// </summary>
    public sealed record ResolveItemCodesRequest(List<string>? Codes);

    [HttpPost]
    public async Task<IActionResult> ResolveItemCodes([FromBody] ResolveItemCodesRequest? request, CancellationToken ct)
    {
        var codes = (request?.Codes ?? new List<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();
        if (codes.Count == 0) return Json(Array.Empty<object>());

        var items = await _logisticsConfigurationService.GetItemsForLookupAsync(ct);
        var byCode = items
            .GroupBy(i => i.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var result = codes.Select(c =>
        {
            if (byCode.TryGetValue(c, out var it))
                return new ResolvedItemCodeDto(c, true, it.Id, it.Code.Trim(), it.Name, it.Combinations);
            return new ResolvedItemCodeDto(c, false, 0, null, null, false);
        });
        return Json(result);
    }

    private sealed record ResolvedItemCodeDto(
        string Code, bool Found, int Id, string? ResolvedCode, string? Name, bool HasConfig);
}

