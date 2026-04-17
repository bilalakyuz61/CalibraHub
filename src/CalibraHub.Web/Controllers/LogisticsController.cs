using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Ui;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Models.Logistics;
using CalibraHub.Web.Models.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class LogisticsController : Controller
{
    private readonly ILogisticsConfigurationService _logisticsConfigurationService;
    private readonly IUiConfigurationService _uiConfigurationService;
    private readonly IIntegrationEventService _integrationEventService;
    private readonly IFinanceService _financeService;
    private readonly IWidgetService _widgetService;

    public LogisticsController(
        ILogisticsConfigurationService logisticsConfigurationService,
        IUiConfigurationService uiConfigurationService,
        IIntegrationEventService integrationEventService,
        IFinanceService financeService,
        IWidgetService widgetService)
    {
        _logisticsConfigurationService = logisticsConfigurationService;
        _uiConfigurationService = uiConfigurationService;
        _integrationEventService = integrationEventService;
        _financeService = financeService;
        _widgetService = widgetService;
    }

    private int GetCompanyId()
    {
        var raw = User.FindFirstValue("company_id");
        return int.TryParse(raw, out var id) ? id : 0;
    }

    [HttpGet]
    public async Task<IActionResult> MaterialCards(CancellationToken cancellationToken)
    {
        var boardConfig = await BuildMaterialCardsBoardConfigAsync(cancellationToken);

        var viewModel = new MaterialCardsViewModel
        {
            MaterialCards = [],
            MaterialCardLookup = [],
            ListState = new MaterialCardListStateViewModel
            {
                GridKey = "logistics-material-cards",
                Page = 1,
                PageSize = MaterialCardPageSize,
                TotalCount = 0,
                TotalPages = 0,
                ItemLabel = "malzeme karti",
                PageSizeOptions = BuildPageSizeOptions(MaterialCardPageSize)
            },
            StockInput = new MaterialCardCreateInput(),
            AvailableColumns = MaterialCardGridColumns,
            VisibleColumns = await GetMaterialCardVisibleColumnsAsync(cancellationToken),
            BoardConfig = boardConfig
        };
        return View(viewModel);
    }

    /// <summary>
    /// CalibraSmartBoard icin inline JSON config hazirlar.
    /// Tum mantik server-side: entity basliklari, widget listesi, link URL'leri
    /// ve aksiyon butonlari burada kararlastirilir. React tarafi yalniz cizer.
    /// </summary>
    private const int MaterialCardPageSize = 50;

    private async Task<object> BuildMaterialCardsBoardConfigAsync(CancellationToken ct)
    {
        var (cards, totalCount) = await _logisticsConfigurationService.GetItemsPagedAsync(null, 0, MaterialCardPageSize, ct);
        var masterWidgets = await BuildItemsMasterWidgetsAsync(ct);
        var entities = await BuildMaterialCardEntitiesAsync(cards, ct);

        return new
        {
            boardKey = "logistics-material-cards",
            title = "Malzeme Kartlari",
            subtitle = totalCount.ToString("N0") + " malzeme",
            icon = "Package",
            iconColor = "indigo",
            searchPlaceholder = "Malzeme ara... (kod, isim)",
            emptyText = "Henuz malzeme eklenmemis",
            apiUrl = "/Logistics/GetMaterialCardsPage",
            totalCount,
            pageSize = MaterialCardPageSize,
            actions = new[]
            {
                new
                {
                    id = "new",
                    label = "Yeni Malzeme",
                    icon = "Package",
                    variant = "primary",
                    url = "/Logistics/MaterialCardEdit"
                }
            },
            masterWidgets,
            entities
        };
    }

    // GET /Logistics/GetMaterialCardsPage?page=2&pageSize=50&search=abc
    [HttpGet]
    public async Task<IActionResult> GetMaterialCardsPage(
        int page = 1, int pageSize = 50, string? search = null, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 10) pageSize = 10;
        if (pageSize > 200) pageSize = 200;
        var offset = (page - 1) * pageSize;

        try
        {
            var (cards, totalCount) = await _logisticsConfigurationService.GetItemsPagedAsync(search, offset, pageSize, ct);
            var entities = await BuildMaterialCardEntitiesAsync(cards, ct);
            return Json(new { entities, totalCount, page, pageSize });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    private async Task<List<object>> BuildItemsMasterWidgetsAsync(CancellationToken ct)
    {
        var masterWidgets = new List<object>();
        var itemsSchema = await _widgetService.GetFormSchemaByCodeAsync("ITEMS", ct);
        if (itemsSchema != null)
        {
            foreach (var w in itemsSchema.Widgets.Where(w => w.IsActive
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

    private async Task<List<object>> BuildMaterialCardEntitiesAsync(
        IReadOnlyCollection<ItemDto> cards, CancellationToken ct)
    {
        var recordIds = cards.Select(c => c.Id.ToString()).ToArray();
        var batchWidgets = recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("ITEMS", recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<CalibraHub.Application.Contracts.WidgetRenderDto>>();

        var entities = new List<object>();
        foreach (var card in cards)
        {
            string? cardImageUrl = (card.ImageData != null && card.ImageData.Length > 0 && !string.IsNullOrWhiteSpace(card.ImageMimeType))
                ? $"data:{card.ImageMimeType};base64,{Convert.ToBase64String(card.ImageData)}"
                : null;

            var cardWidgets = new List<object>();
            var recordId = card.Id.ToString();
            if (batchWidgets.TryGetValue(recordId, out var renderDtos))
            {
                foreach (var w in renderDtos)
                {
                    cardWidgets.Add(new {
                        id             = w.WidgetId,
                        type           = "data",
                        dataType       = w.DataType.ToLowerInvariant(),
                        label          = w.Label,
                        value          = w.Value,
                        isPlainField   = w.IsPlainField,
                        minLength      = w.MinLength,
                        expectedLength = w.ExpectedLength,
                        maxLength      = w.MaxLength,
                        minValue       = w.MinValue,
                        maxValue       = w.MaxValue,
                        colorType      = w.ColorType,
                        colorValue     = w.ColorValue,
                    });
                }
            }

            entities.Add(new {
                id = card.Id,
                title = card.MaterialName ?? "(adsiz)",
                subtitle = card.MaterialCode ?? string.Empty,
                description = card.MaterialDescription ?? string.Empty,
                imageUrl = cardImageUrl,
                statusBadge = (object?)null,
                widgets = cardWidgets,
                primaryAction = new {
                    label = "Duzenle",
                    icon = "Edit",
                    url = $"/Logistics/MaterialCardEdit?id={card.Id}"
                },
                secondaryAction = new {
                    label = "Sil",
                    icon = "Trash2",
                    apiUrl = $"/Logistics/DeleteMaterialCardJson?id={card.Id}",
                    confirm = "Bu malzeme kartini silmek istediginizden emin misiniz?"
                }
            });
        }
        return entities;
    }

    [HttpGet]
    public async Task<IActionResult> MaterialCardEdit(int? id, CancellationToken cancellationToken)
    {
        ViewData["MaterialCardEditId"] = id ?? 0;

        // Yeni EAV widget renderer icin integer Id - ViewBag'e aktar.
        ViewBag.ItemId = id.HasValue && id.Value > 0 ? id.Value.ToString() : string.Empty;

        return View();
    }
    
    [HttpGet]
    public async Task<IActionResult> BOMs(CancellationToken cancellationToken)
    {
        ViewBag.Title = "ÃœrÃ¼n AÄŸacÄ±";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetBOM(
        string materialCode,
        string? configCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            return BadRequest(new { found = false });

        var tree = await _logisticsConfigurationService.GetBOMByCodeAsync(materialCode, configCode, cancellationToken);
        if (tree is null)
            return Ok(new { found = false });

        return Ok(new
        {
            found         = true,
            id            = tree.Id,
            description   = tree.Description,
            imageBase64   = tree.ImageData   != null ? Convert.ToBase64String(tree.ImageData) : null,
            imageMimeType = tree.ImageMimeType,
            imageFitMode  = tree.ImageFitMode ?? "square",
            lines         = tree.Lines.Select(l => new
            {
                componentMaterialCode = l.ComponentMaterialCode,
                componentMaterialName = l.ComponentMaterialName,
                componentConfigCode   = l.ComponentConfigCode,
                quantity              = l.Quantity,
                scrapRatio            = l.ScrapRatio
            })
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveBOM(
        [FromBody] SaveBOMRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { success = false, message = "GeÃ§ersiz istek." });

        try
        {
            var id = await _logisticsConfigurationService.SaveBOMAsync(request, cancellationToken);
            return Ok(new { success = true, id });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> StockLookup(string? q, CancellationToken cancellationToken)
    {
        var cards = await _logisticsConfigurationService.GetItemsForLookupAsync(cancellationToken);
        var query = (q ?? "").Trim().ToLowerInvariant();
        var filtered = cards
            .Where(s => string.IsNullOrEmpty(query)
                     || s.MaterialCode.ToLowerInvariant().Contains(query)
                     || (s.MaterialName?.ToLowerInvariant().Contains(query) ?? false));

        if (!string.IsNullOrEmpty(query))
            filtered = filtered.Take(50);

        var results = filtered
            .Select(s => new
            {
                code      = s.MaterialCode.Trim(),
                name      = s.MaterialName,
                hasConfig = s.TrackCombinations
            });
        return Json(results);
    }

    [HttpGet]
    public async Task<IActionResult> CombinationLookup(string materialCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            return Json(Array.Empty<object>());
        var combos = await _logisticsConfigurationService.GetCombinationsForLookupAsync(materialCode.Trim(), cancellationToken);
        return Json(combos.Select(c => new
        {
            code   = c.Code,
            name   = c.Name,
            features = c.FeatureValues.Select(fv => new { feature = fv.Feature, value = fv.Value }).ToArray()
        }));
    }


    [HttpGet]
    public IActionResult Locations() => View();

    [HttpGet]
    public IActionResult Units() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLocation(
        [Bind(Prefix = "LocationInput")] LocationInput input,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildLocationsViewModelAsync(input, search, page, pageSize, cancellationToken);
            return View(nameof(Locations), invalidModel);
        }

        try
        {
            if (input.Id.HasValue)
            {
                await _logisticsConfigurationService.UpdateLocationAsync(
                    new UpdateLocationRequest(
                        input.Id.Value,
                        input.ParentId,
                        input.LocationTypeCode,
                        input.LocationCode,
                        input.LocationName,
                        input.SortOrder,
                        input.MaxWeightCapacity,
                        input.VolumeCapacity,
                        input.IsActive),
                    cancellationToken);

                TempData["AdminSuccess"] = "Lokasyon kaydi guncellendi.";
            }
            else
            {
                await _logisticsConfigurationService.CreateLocationAsync(
                    new CreateLocationRequest(
                        input.ParentId,
                        input.LocationTypeCode,
                        input.LocationCode,
                        input.LocationName,
                        input.SortOrder,
                        input.MaxWeightCapacity,
                        input.VolumeCapacity,
                        input.IsActive),
                    cancellationToken);

                TempData["AdminSuccess"] = "Lokasyon kaydi olusturuldu.";
            }

            return RedirectToAction(nameof(Locations), new { search, page, pageSize });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildLocationsViewModelAsync(input, search, page, pageSize, cancellationToken);
            return View(nameof(Locations), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveUnit(
        [Bind(Prefix = "Input")] UnitInput input,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildUnitsViewModelAsync(input, search, page, pageSize, cancellationToken);
            return View(nameof(Units), invalidModel);
        }

        try
        {
            if (input.Id.HasValue)
            {
                await _logisticsConfigurationService.UpdateUnitAsync(
                    new UpdateUnitRequest(
                        input.Id.Value,
                        input.UnitCode,
                        input.UnitName,
                        input.IntlCode,
                        input.SortOrder,
                        input.IsActive),
                    cancellationToken);

                TempData["AdminSuccess"] = "Olcu birimi kaydi guncellendi.";
            }
            else
            {
                await _logisticsConfigurationService.CreateUnitAsync(
                    new CreateUnitRequest(
                        input.UnitCode,
                        input.UnitName,
                        input.IntlCode,
                        input.SortOrder,
                        input.IsActive),
                    cancellationToken);

                TempData["AdminSuccess"] = "Olcu birimi kaydi olusturuldu.";
            }

            return RedirectToAction(nameof(Units), new { search, page, pageSize });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildUnitsViewModelAsync(input, search, page, pageSize, cancellationToken);
            return View(nameof(Units), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLocation(
        int id,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            await _logisticsConfigurationService.DeleteLocationAsync(id, cancellationToken);
            TempData["AdminSuccess"] = "Lokasyon kaydi silindi.";
            return RedirectToAction(nameof(Locations), new { search, page, pageSize });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildLocationsViewModelAsync(new LocationInput(), search, page, pageSize, cancellationToken);
            return View(nameof(Locations), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUnit(
        int id,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            await _logisticsConfigurationService.DeleteUnitAsync(id, cancellationToken);
            TempData["AdminSuccess"] = "Olcu birimi kaydi silindi.";
            return RedirectToAction(nameof(Units), new { search, page, pageSize });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildUnitsViewModelAsync(new UnitInput(), search, page, pageSize, cancellationToken);
            return View(nameof(Units), invalidModel);
        }
    }

    /* â"€â"€ Ã–lÃ§Ã¼ Birimi JSON Endpoint'leri â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€ */

    [HttpGet]
    public async Task<IActionResult> GetAllMeasureUnits(string? search, CancellationToken ct)
    {
        var all = await _logisticsConfigurationService.GetUnitsAsync(ct);
        var filtered = all
            .OrderBy(x => x.SortOrder).ThenBy(x => x.UnitCode, StringComparer.OrdinalIgnoreCase)
            .Where(x =>
                string.IsNullOrWhiteSpace(search) ||
                ContainsInsensitive(x.UnitCode, search) ||
                ContainsInsensitive(x.UnitName, search))
            .Select(x => new { x.Id, x.UnitCode, x.UnitName, x.IntlCode, x.SortOrder, x.IsActive });
        return Json(filtered);
    }

    [HttpGet]
    public async Task<IActionResult> GetMeasureUnit(int id, CancellationToken ct)
    {
        var all = await _logisticsConfigurationService.GetUnitsAsync(ct);
        var item = all.FirstOrDefault(x => x.Id == id);
        if (item is null) return NotFound();
        return Json(new { item.Id, item.UnitCode, item.UnitName, item.IntlCode, item.SortOrder, item.IsActive });
    }

    [HttpPost]
    public async Task<IActionResult> SaveMeasureUnitJson([FromBody] UnitInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.UnitCode) || string.IsNullOrWhiteSpace(input.UnitName))
            return Json(new { success = false, message = "Kod ve ad bos olamaz." });
        try
        {
            if (input.Id.HasValue && input.Id.Value > 0)
            {
                await _logisticsConfigurationService.UpdateUnitAsync(
                    new UpdateUnitRequest(input.Id.Value, input.UnitCode, input.UnitName, input.IntlCode, input.SortOrder, input.IsActive), ct);
            }
            else
            {
                await _logisticsConfigurationService.CreateUnitAsync(
                    new CreateUnitRequest(input.UnitCode, input.UnitName, input.IntlCode, input.SortOrder, input.IsActive), ct);
            }
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteMeasureUnitJson(int id, CancellationToken ct)
    {
        try
        {
            await _logisticsConfigurationService.DeleteUnitAsync(id, ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    /* â"€â"€ Lokasyon JSON Endpoint'leri â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€ */

    [HttpGet]
    public async Task<IActionResult> GetAllLocations(string? search, CancellationToken ct)
    {
        var all = await _logisticsConfigurationService.GetLocationsAsync(ct);
        var lookup = all.ToDictionary(x => x.Id);
        var filtered = all
            .OrderBy(x => x.SortOrder).ThenBy(x => x.LocationCode, StringComparer.OrdinalIgnoreCase)
            .Where(x =>
                string.IsNullOrWhiteSpace(search) ||
                ContainsInsensitive(GetLocationTypeDisplayName(x.LocationTypeCode), search) ||
                ContainsInsensitive(x.LocationCode, search) ||
                ContainsInsensitive(x.LocationName ?? string.Empty, search) ||
                ContainsInsensitive(
                    x.ParentId.HasValue && lookup.TryGetValue(x.ParentId.Value, out var p)
                        ? string.IsNullOrWhiteSpace(p.LocationName) ? p.LocationCode : $"{p.LocationCode} {p.LocationName}"
                        : string.Empty, search))
            .Select(x =>
            {
                var parent = x.ParentId.HasValue ? lookup.GetValueOrDefault(x.ParentId.Value) : null;
                return new
                {
                    x.Id, x.ParentId,
                    x.LocationTypeCode,
                    locationTypeDisplayName = GetLocationTypeDisplayName(x.LocationTypeCode),
                    x.LocationCode,
                    locationName = x.LocationName ?? string.Empty,
                    parentDisplayName = parent is null ? "-"
                        : string.IsNullOrWhiteSpace(parent.LocationName) ? parent.LocationCode
                        : $"{parent.LocationCode} - {parent.LocationName}",
                    x.SortOrder, x.MaxWeightCapacity, x.VolumeCapacity, x.IsActive
                };
            });
        return Json(filtered);
    }

    [HttpGet]
    public async Task<IActionResult> GetLocation(int id, CancellationToken ct)
    {
        var all = await _logisticsConfigurationService.GetLocationsAsync(ct);
        var item = all.FirstOrDefault(x => x.Id == id);
        if (item is null) return NotFound();
        return Json(new
        {
            item.Id, item.ParentId,
            locationTypeCode = NormalizeLocationTypeCode(item.LocationTypeCode),
            item.LocationCode,
            locationName = item.LocationName ?? string.Empty,
            item.SortOrder, item.MaxWeightCapacity, item.VolumeCapacity, item.IsActive
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveLocationJson([FromBody] LocationInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.LocationTypeCode) || string.IsNullOrWhiteSpace(input.LocationCode))
            return Json(new { success = false, message = "Lokasyon tipi ve kod bos olamaz." });
        try
        {
            var typeCode = NormalizeLocationTypeCode(input.LocationTypeCode);
            if (input.Id.HasValue && input.Id.Value > 0)
            {
                await _logisticsConfigurationService.UpdateLocationAsync(
                    new UpdateLocationRequest(input.Id.Value, input.ParentId, typeCode, input.LocationCode,
                        input.LocationName, input.SortOrder, input.MaxWeightCapacity, input.VolumeCapacity, input.IsActive), ct);
            }
            else
            {
                await _logisticsConfigurationService.CreateLocationAsync(
                    new CreateLocationRequest(input.ParentId, typeCode, input.LocationCode,
                        input.LocationName, input.SortOrder, input.MaxWeightCapacity, input.VolumeCapacity, input.IsActive), ct);
            }
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteLocationJson(int id, CancellationToken ct)
    {
        try
        {
            await _logisticsConfigurationService.DeleteLocationAsync(id, ct);
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    /* â"€â"€ Malzeme GruplarÄ± â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€ */

    [HttpGet]
    public IActionResult MaterialGroups()
        => View(new MaterialGroupsViewModel());

    /// <summary>Belirli bir kategoriye ait grup listesini JSON dÃ¶ner.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAllMaterialGroups(int? category, CancellationToken cancellationToken)
    {
        var groups = await _logisticsConfigurationService.GetMaterialGroupsAsync(category, cancellationToken);
        return Json(groups.Select(g => new { id = g.Id, category = g.GroupCategory, code = g.GroupCode, description = g.GroupDescription ?? string.Empty }));
    }

    /// <summary>Grup oluÅŸtur/gÃ¼ncelle (JSON body, kategori zorunlu).</summary>
    [HttpPost]
    public async Task<IActionResult> UpsertMaterialGroup(
        [FromBody] SaveMaterialGroupRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { success = false, message = "GeÃ§ersiz istek." });
        try
        {
            if (request.Id is > 0)
                await _logisticsConfigurationService.UpdateMaterialGroupAsync(request, cancellationToken);
            else
                await _logisticsConfigurationService.CreateMaterialGroupAsync(request, cancellationToken);
            var groups = await _logisticsConfigurationService.GetMaterialGroupsAsync(request.GroupCategory, cancellationToken);
            return Ok(new { success = true, groups = groups.Select(g => new { id = g.Id, category = g.GroupCategory, code = g.GroupCode, description = g.GroupDescription ?? string.Empty }) });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>Grup sil - kategorideki gÃ¼ncel listeyi dÃ¶ner.</summary>
    [HttpPost]
    public async Task<IActionResult> DeleteMaterialGroupInline(
        [FromBody] DeleteMaterialGroupBody body,
        CancellationToken cancellationToken)
    {
        try
        {
            await _logisticsConfigurationService.DeleteMaterialGroupAsync(body.Id, cancellationToken);
            var groups = await _logisticsConfigurationService.GetMaterialGroupsAsync(body.Category, cancellationToken);
            return Ok(new { success = true, groups = groups.Select(g => new { id = g.Id, category = g.GroupCategory, code = g.GroupCode, description = g.GroupDescription ?? string.Empty }) });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>Malzeme kartÄ± iÃ§in grup kodu arama (kategori filtreli).</summary>
    [HttpGet]
    public async Task<IActionResult> MaterialGroupLookup(int? category, string? q, CancellationToken cancellationToken)
    {
        var groups = await _logisticsConfigurationService.GetMaterialGroupsAsync(category, cancellationToken);
        var q2 = q?.Trim() ?? string.Empty;
        var result = groups
            .Where(g => string.IsNullOrWhiteSpace(q2) ||
                        g.GroupCode.Contains(q2, StringComparison.OrdinalIgnoreCase) ||
                        (g.GroupDescription?.Contains(q2, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(g => g.GroupCode)
            .Take(30)
            .Select(g => new { code = g.GroupCode, description = g.GroupDescription ?? string.Empty })
            .ToArray();
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetMaterialGroupMappings(int stockCardId, CancellationToken cancellationToken)
    {
        var mappings = await _logisticsConfigurationService.GetMaterialGroupMappingsAsync(stockCardId, cancellationToken);
        return Json(mappings.Select(m => new { slotOrder = m.SlotOrder, groupCode = m.GroupCode, groupDescription = m.GroupDescription ?? string.Empty }));
    }

    [HttpPost]
    public async Task<IActionResult> SaveMaterialGroupMappings(
        [FromBody] SaveMaterialGroupMappingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { success = false, message = "GeÃ§ersiz istek." });
        try
        {
            await _logisticsConfigurationService.SaveMaterialGroupMappingsAsync(request, cancellationToken);
            return Ok(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMaterialCard(
        [Bind(Prefix = "StockInput")] MaterialCardCreateInput stockInput,
        IFormFile? ProductImage,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildMaterialCardsViewModelAsync(
                stockInput,
                await GetCurrentMaterialCardListQueryAsync(cancellationToken),
                cancellationToken);
            return View(nameof(MaterialCards), invalidModel);
        }

        try
        {
            if (!stockInput.ItemId.HasValue || stockInput.ItemId.Value == 0)
            {
                var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(cancellationToken);
                var existingMaterialCard = snapshot.Items.FirstOrDefault(x =>
                    string.Equals(x.MaterialCode, stockInput.MaterialCode.Trim(), StringComparison.OrdinalIgnoreCase));

                if (existingMaterialCard is not null)
                {
                    stockInput.ItemId = existingMaterialCard.Id;
                }
            }

            var isUpdate = stockInput.ItemId.HasValue && stockInput.ItemId.Value != 0;
            var currentUserName = User.FindFirstValue(System.Security.Claims.ClaimTypes.Name);

            byte[]? imageData = null;
            string? imageMimeType = null;
            
            if (!string.IsNullOrEmpty(stockInput.ProductImageBase64))
            {
                var base64Data = stockInput.ProductImageBase64.Substring(stockInput.ProductImageBase64.IndexOf(",") + 1);
                imageData = Convert.FromBase64String(base64Data);
                imageMimeType = "image/jpeg";
            }
            else if (ProductImage != null && ProductImage.Length > 0)
            {
                using var memoryStream = new MemoryStream();
                await ProductImage.CopyToAsync(memoryStream, cancellationToken);
                imageData = memoryStream.ToArray();
                imageMimeType = ProductImage.ContentType;
            }

            var companyId = GetCompanyId();
            var placeholders = new Dictionary<string, string>
            {
                ["EntityId"] = (stockInput.ItemId ?? 0).ToString(),
                ["UserName"] = User.FindFirstValue(System.Security.Claims.ClaimTypes.Name) ?? "system",
                ["MaterialCode"] = stockInput.MaterialCode ?? "",
                ["MaterialName"] = stockInput.MaterialName ?? "",
                ["MaterialDescription"] = stockInput.MaterialDescription ?? "",
                ["MaterialTypeId"] = stockInput.MaterialTypeId?.ToString() ?? "",
                ["IsActive"] = "1"
            };

            if (isUpdate)
            {
                placeholders["EntityId"] = stockInput.ItemId!.Value.ToString();
                await _integrationEventService.ExecuteBeforeEventAsync(companyId, "Item", "BeforeUpdate", placeholders, cancellationToken);

                await _logisticsConfigurationService.UpdateItemAsync(
                    new UpdateItemRequest(
                        ItemId: stockInput.ItemId!.Value,
                        MaterialCode: stockInput.MaterialCode,
                        MaterialName: stockInput.MaterialName,
                        MaterialDescription: stockInput.MaterialDescription,
                        MaterialTypeId: stockInput.MaterialTypeId,
                        TrackCombinations: stockInput.TrackCombinations,
                        ImageData: imageData,
                        ImageMimeType: imageMimeType),
                    cancellationToken);

                _integrationEventService.FireAfterEvent(companyId, "Item", "AfterUpdate", placeholders);
            }
            else
            {
                await _integrationEventService.ExecuteBeforeEventAsync(companyId, "Item", "BeforeCreate", placeholders, cancellationToken);

                await _logisticsConfigurationService.CreateItemAsync(
                    new CreateItemRequest(
                        MaterialCode: stockInput.MaterialCode,
                        MaterialName: stockInput.MaterialName,
                        MaterialDescription: stockInput.MaterialDescription,
                        MaterialTypeId: stockInput.MaterialTypeId,
                        TrackCombinations: stockInput.TrackCombinations,
                        ImageData: imageData,
                        ImageMimeType: imageMimeType),
                    cancellationToken);

                _integrationEventService.FireAfterEvent(companyId, "Item", "AfterCreate", placeholders);
            }

            // Kaydedilen/guncellenen kartin ID'sini bul
            var savedCardId = stockInput.ItemId;
            if (!isUpdate && (savedCardId == null || savedCardId == 0))
            {
                var refreshed = await _logisticsConfigurationService.GetSnapshotAsync(cancellationToken);
                var created = refreshed.Items
                    .FirstOrDefault(x => string.Equals(x.MaterialCode, stockInput.MaterialCode, StringComparison.OrdinalIgnoreCase));
                if (created != null) savedCardId = created.Id;
            }

            TempData["AdminSuccess"] = isUpdate
                ? "Malzeme karti guncellendi."
                : "Malzeme karti kaydedildi.";
            return RedirectToMaterialCards(new { id = savedCardId });
        }
        catch (CalibraHub.Application.Services.IntegrationEventException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var ieModel = await BuildMaterialCardsViewModelAsync(
                stockInput,
                await GetCurrentMaterialCardListQueryAsync(cancellationToken),
                cancellationToken);
            return View(nameof(MaterialCards), ieModel);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ToMaterialMessage(ex.Message));
            var invalidModel = await BuildMaterialCardsViewModelAsync(
                stockInput,
                await GetCurrentMaterialCardListQueryAsync(cancellationToken),
                cancellationToken);
            return View(nameof(MaterialCards), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMaterialCard(
        int stockCardId,
        CancellationToken cancellationToken)
    {
        try
        {
            var companyId = GetCompanyId();
            // Silinecek kartin bilgilerini placeholder'lara ekle
            var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(cancellationToken);
            var card = snapshot.Items.FirstOrDefault(x => x.Id == stockCardId);
            var placeholders = new Dictionary<string, string>
            {
                ["EntityId"] = stockCardId.ToString(),
                ["UserName"] = User.FindFirstValue(System.Security.Claims.ClaimTypes.Name) ?? "system",
                ["MaterialCode"] = card?.MaterialCode ?? "",
                ["MaterialName"] = card?.MaterialName ?? "",
                ["MaterialDescription"] = card?.MaterialDescription ?? "",
                ["MaterialTypeId"] = card?.MaterialTypeId?.ToString() ?? "",
                ["IsActive"] = card?.IsActive == true ? "1" : "0"
            };

            await _integrationEventService.ExecuteBeforeEventAsync(companyId, "Item", "BeforeDelete", placeholders, cancellationToken);
            await _logisticsConfigurationService.DeactivateItemAsync(stockCardId, cancellationToken);
            _integrationEventService.FireAfterEvent(companyId, "Item", "AfterDelete", placeholders);

            if (IsAjaxRequest(Request))
            {
                return Json(new
                {
                    success = true,
                    message = "Malzeme karti silindi."
                });
            }

            return RedirectToMaterialCards();
        }
        catch (CalibraHub.Application.Services.IntegrationEventException ex)
        {
            if (IsAjaxRequest(Request))
                return BadRequest(new { success = false, message = ex.Message });

            ModelState.AddModelError(string.Empty, ex.Message);
            var ieModel = await BuildMaterialCardsViewModelAsync(
                new MaterialCardCreateInput(),
                await GetCurrentMaterialCardListQueryAsync(cancellationToken),
                cancellationToken);
            return View(nameof(MaterialCards), ieModel);
        }
        catch (ArgumentException ex)
        {
            var materialMessage = ToMaterialMessage(ex.Message);

            if (IsAjaxRequest(Request))
            {
                return BadRequest(new
                {
                    success = false,
                    message = materialMessage
                });
            }

            ModelState.AddModelError(string.Empty, ToMaterialMessage(ex.Message));
            var invalidModel = await BuildMaterialCardsViewModelAsync(
                new MaterialCardCreateInput(),
                await GetCurrentMaterialCardListQueryAsync(cancellationToken),
                cancellationToken);
            return View(nameof(MaterialCards), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfigureMaterialCard(
        int stockCardId,
        bool isConfigurable,
        List<Guid>? propertyIds,
        CancellationToken cancellationToken)
    {
        try
        {
            await _logisticsConfigurationService.ConfigureItemAsync(
                new ConfigureItemRequest(
                    stockCardId,
                    isConfigurable,
                    (propertyIds ?? new List<Guid>()).ToArray()),
                cancellationToken);

            TempData["AdminSuccess"] = "Malzeme karti yapilandirma ayarlari guncellendi.";
            return RedirectToMaterialCards();
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ToMaterialMessage(ex.Message));
            var invalidModel = await BuildMaterialCardsViewModelAsync(
                new MaterialCardCreateInput(),
                await GetCurrentMaterialCardListQueryAsync(cancellationToken),
                cancellationToken);
            return View(nameof(MaterialCards), invalidModel);
        }
    }

    [HttpGet]
    public async Task<IActionResult> ProductConfiguration(CancellationToken cancellationToken)
    {
        // Yeni akis: SmartBoard cart listesi. Eski 7 parametreli tab/pagination
        // yaklasimi kaldirildi - SmartBoard kendi UI state'ini yonetiyor.
        // Eski SaveProductFeature / UpdateProductFeature / SaveProductValue vb.
        // action'lari hala DB'ye yaziyor ama artik UI'dan cagrilmiyor - yeni
        // ProductFeatureEdit sayfasi kullaniliyor.
        var boardConfig = await BuildProductConfigurationBoardConfigAsync(cancellationToken);
        return View(new ProductConfigurationViewModel { BoardConfig = boardConfig });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // BuildProductConfigurationBoardConfigAsync
    //
    // Urun Konfigurasyonu (Features/Ozellikler) icin SmartBoard kart config'i
    // uretir. Her feature bir kart - icinde DataType, deger sayisi, ornek
    // degerler, bagli stok sayisi, aktif/pasif durumu widget'lari. Admin
    // panelden sales_quotes/contact_accounts gibi dynamic widget tanimlamak
    // icin "product_configuration" screenCode'u ile schema cagrisi yapilir.
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    private async Task<object> BuildProductConfigurationBoardConfigAsync(CancellationToken ct)
    {
        var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(ct);
        var features = snapshot.Features.Where(f => f.IsActive).OrderBy(f => f.Name).ToArray();

        // Feature-stock linkleri - ProductConfigurationItemDto.FeatureId nullable int
        var stockLinksByFeature = new Dictionary<int, List<string>>();
        if (snapshot.Configurations != null)
        {
            foreach (var cfg in snapshot.Configurations.Where(c => c.IsActive && c.FeatureId.HasValue))
            {
                var fid = cfg.FeatureId!.Value;
                if (!stockLinksByFeature.TryGetValue(fid, out var list))
                {
                    list = new List<string>();
                    stockLinksByFeature[fid] = list;
                }
                if (!string.IsNullOrWhiteSpace(cfg.RelatedMaterialCode)
                    && !list.Contains(cfg.RelatedMaterialCode))
                {
                    list.Add(cfg.RelatedMaterialCode);
                }
            }
        }

        var entities = new List<object>();
        foreach (var feature in features)
        {
            var featureValues = snapshot.Values
                .Where(v => v.FeatureId == feature.Id && v.IsActive)
                .OrderBy(v => v.Description)
                .ToArray();

            var stockCodes = stockLinksByFeature.TryGetValue(feature.Id, out var codes)
                ? codes
                : new List<string>();

            var widgets = new List<object>();

            // â"€â"€ Sistem widget'lari â"€â"€
            widgets.Add(new
            {
                id = "sys_datatype",
                type = "data",
                dataType = "text",
                label = "Veri Tipi",
                value = TranslateDataType(feature.DataType),
                color = DataTypeColor(feature.DataType),
            });

            if (!string.IsNullOrWhiteSpace(feature.UnitOfMeasure))
            {
                widgets.Add(new
                {
                    id = "sys_unit",
                    type = "data",
                    dataType = "text",
                    label = "Olcu Birimi",
                    value = feature.UnitOfMeasure,
                    color = "slate",
                });
            }

            widgets.Add(new
            {
                id = "sys_value_count",
                type = "data",
                dataType = "numeric",
                label = "Deger Sayisi",
                value = featureValues.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                detail = featureValues.Length > 0 ? "deger" : "bos",
                color = featureValues.Length > 0 ? "emerald" : "amber",
            });

            // Ilk 3 degeri ornek olarak goster
            if (featureValues.Length > 0)
            {
                var sampleValues = featureValues
                    .Take(3)
                    .Select(v => v.Description ?? v.Value ?? v.Code)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();
                var sampleText = string.Join(", ", sampleValues);
                if (featureValues.Length > 3) sampleText += "...";
                widgets.Add(new
                {
                    id = "sys_value_sample",
                    type = "data",
                    dataType = "text",
                    label = "Ornek Degerler",
                    value = sampleText,
                    color = "cyan",
                });
            }

            widgets.Add(new
            {
                id = "sys_stock_count",
                type = "data",
                dataType = "numeric",
                label = "Bagli Stok",
                value = stockCodes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                detail = stockCodes.Count > 0 ? "malzeme" : "bos",
                color = stockCodes.Count > 0 ? "blue" : "slate",
            });

            // Admin dinamik widget'lari yeni WidgetMas/WidgetTra altyapisina tasindi -
            // SmartBoard entegrasyonu ileride IWidgetService uzerinden eklenecek.

            entities.Add(new
            {
                id = feature.Id,
                title = feature.Name ?? "(isimsiz)",
                subtitle = feature.Code ?? string.Empty,
                description = string.Empty,
                imageUrl = (string?)null,
                statusBadge = (object?)null,
                widgets,
                primaryAction = new
                {
                    label = "Duzenle",
                    icon = "Edit",
                    url = $"/Logistics/ProductFeatureEdit?id={feature.Id}",
                },
                secondaryAction = new
                {
                    label = "Sil",
                    icon = "Trash2",
                    apiUrl = $"/Logistics/DeleteProductFeatureJson?id={feature.Id}",
                    confirm = $"Bu ozelligi silmek istediginizden emin misiniz? ({feature.Name})",
                },
            });
        }

        // â"€â"€ Master widget sablonu (âš™ SmartBoardConfigPanel icin) â"€â"€
        // Sistem widget'lari sabit; PRODUCT_CONFIG form kodundaki admin widget'lar ekleniyor.
        var masterWidgets = new List<object>
        {
            new { id = "sys_datatype",     type = "data", dataType = "text",    label = "Veri Tipi" },
            new { id = "sys_unit",         type = "data", dataType = "text",    label = "Olcu Birimi" },
            new { id = "sys_value_count",  type = "data", dataType = "numeric", label = "Deger Sayisi" },
            new { id = "sys_value_sample", type = "data", dataType = "text",    label = "Ornek Degerler" },
            new { id = "sys_stock_count",  type = "data", dataType = "numeric", label = "Bagli Stok" },
        };
        var pcSchema = await _widgetService.GetFormSchemaByCodeAsync("PRODUCT_CONFIG", ct);
        if (pcSchema != null)
        {
            foreach (var w in pcSchema.Widgets.Where(w => w.IsActive && !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)))
            {
                masterWidgets.Add(new
                {
                    id = w.WidgetCode,
                    type = "data",
                    dataType = w.DataType.ToLowerInvariant(),
                    label = w.Label,
                });
            }
        }

        return new
        {
            boardKey = "product-configuration",
            title = "Ozellik ve Kombinasyon",
            subtitle = $"{entities.Count} ozellik",
            icon = "Sliders",
            iconColor = "teal",
            searchPlaceholder = "Ozellik ara... (ad, kod)",
            emptyText = "Henuz ozellik tanimlanmamis",
            actions = new[]
            {
                new
                {
                    id = "new",
                    label = "Yeni Ozellik",
                    icon = "Plus",
                    variant = "primary",
                    url = "/Logistics/ProductFeatureEdit",
                },
                new
                {
                    id = "combinations",
                    label = "Kombinasyon Uretici",
                    icon = "Layers",
                    variant = "secondary",
                    url = "/Logistics/ProductCombinations",
                },
            },
            masterWidgets,
            entities,
        };
    }

    /// <summary>ConfigurationFieldDataType (enum) â†’ Turkce etiket</summary>
    private static string TranslateDataType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Metin";
        return raw.Trim().ToLowerInvariant() switch
        {
            "text"    => "Metin",
            "numeric" => "Sayisal",
            "number"  => "Sayisal",
            "date"    => "Tarih",
            "boolean" => "Evet/Hayir",
            _ => raw,
        };
    }

    /// <summary>DataType â†’ SmartBoard color palette</summary>
    private static string DataTypeColor(string? raw) => raw?.ToLowerInvariant() switch
    {
        "text"    => "blue",
        "numeric" => "amber",
        "number"  => "amber",
        "date"    => "cyan",
        "boolean" => "violet",
        _ => "slate",
    };

    // â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
    // ProductFeatureEdit - yeni edit sayfasi (MaterialCardEdit pattern'i)
    // â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
    [HttpGet]
    public IActionResult ProductFeatureEdit(int? id)
    {
        ViewData["ProductFeatureEditId"] = id ?? 0;
        return View(new ProductFeatureEditViewModel { FeatureId = id });
    }

    /// <summary>
    /// Feature detay fetch - edit sayfasi load'da cagirir.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetProductFeature(int id, CancellationToken ct)
    {
        var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(ct);
        var feature = snapshot.Features.FirstOrDefault(f => f.Id == id);
        if (feature is null) return NotFound();

        var values = snapshot.Values
            .Where(v => v.FeatureId == id && v.IsActive)
            .OrderBy(v => v.Description)
            .Select(v => new
            {
                id = v.Id,
                code = v.Code,
                description = v.Description,
                value = v.Value,
                aciklama = v.Aciklama,
            })
            .ToArray();

        var stockCodes = snapshot.FeatureStockLinks
            .Where(l => l.FeatureId == id && !string.IsNullOrWhiteSpace(l.StockCode))
            .Select(l => l.StockCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Stok adlarini cek (chip etiketi icin)
        var stockCards = await _logisticsConfigurationService.GetItemsForLookupAsync(ct);
        var stockByCode = stockCards
            .GroupBy(s => (s.MaterialCode ?? string.Empty).ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().MaterialName ?? g.First().MaterialCode);

        var linkedStocks = stockCodes
            .Where(code => stockByCode.ContainsKey((code ?? string.Empty).ToUpperInvariant()))
            .Select(code => new
            {
                code = code.Trim(),
                name = stockByCode.TryGetValue((code ?? string.Empty).ToUpperInvariant(), out var n) ? n : code,
            })
            .ToArray();

        return Json(new
        {
            id = feature.Id,
            code = feature.Code,
            name = feature.Name,
            dataType = feature.DataType,
            unitOfMeasure = feature.UnitOfMeasure,
            isActive = feature.IsActive,
            values,
            stockCodes,
            linkedStocks,
        });
    }

    /// <summary>
    /// Feature kaydet - yeni veya guncelleme. Vanilla JS edit formu JSON body POST eder.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SaveProductFeatureJson(
        [FromBody] SaveProductFeatureJsonInput input,
        CancellationToken ct)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.Name))
        {
            return Json(new { success = false, message = "Ozellik adi bos olamaz." });
        }

        if (!Enum.TryParse<ConfigurationFieldDataType>(input.DataType, true, out var dataType))
        {
            return Json(new { success = false, message = "Gecerli bir veri tipi seciniz." });
        }

        try
        {
            int savedId;
            if (input.Id.HasValue && input.Id.Value > 0)
            {
                // Update - mevcut feature
                await _logisticsConfigurationService.UpdateProductConfigurationFeatureAsync(
                    new UpdateProductConfigurationFeatureRequest(
                        input.Id.Value,
                        input.Name.Trim(),
                        dataType,
                        input.UnitOfMeasure),
                    ct);
                savedId = input.Id.Value;
            }
            else
            {
                // Create - yeni feature
                savedId = await _logisticsConfigurationService.CreateProductConfigurationFeatureAsync(
                    new CreateProductConfigurationFeatureRequest(
                        input.Name.Trim(),
                        dataType,
                        input.IsActive,
                        input.UnitOfMeasure),
                    ct);
            }

            return Json(new { success = true, id = savedId });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// SmartCard-uyumlu delete endpoint'i - query string id, body'siz POST.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> DeleteProductFeatureJson(int id, CancellationToken ct)
    {
        try
        {
            await _logisticsConfigurationService.DeleteProductConfigurationFeatureAsync(id, ct);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    public sealed record SaveProductFeatureJsonInput(
        int? Id,
        string Name,
        string DataType,
        string? UnitOfMeasure,
        bool IsActive = true);

    // â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
    // ProductFeatureEdit Phase 2 - Ozellik degeri ekle/sil (JSON)
    // â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

    /// <summary>
    /// Feature'a yeni deger ekler. React/vanilla JS inline form JSON body POST eder.
    /// DataType'a gore Description/TextValue/NumericValue/DateValue'dan biri doldurulur.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SaveProductValueJson(
        [FromBody] SaveProductValueJsonInput input,
        CancellationToken ct)
    {
        if (input is null || input.FeatureId <= 0)
        {
            return Json(new { success = false, message = "Gecersiz istek." });
        }

        try
        {
            var (id, code) = await _logisticsConfigurationService.CreateProductConfigurationValueAsync(
                new CreateProductConfigurationValueRequest(
                    input.FeatureId,
                    input.Description,
                    input.TextValue,
                    input.NumericValue,
                    input.DateValue,
                    true,
                    input.Aciklama),
                ct);
            return Json(new { success = true, id, code });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Feature degeri sil - SmartCard/inline delete ile uyumlu, body'siz query string id.
    /// Mevcut DeleteProductValue action (antiforgery + redirect) dokunulmaz.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> DeleteProductValueJson(int id, CancellationToken ct)
    {
        try
        {
            await _logisticsConfigurationService.DeleteProductConfigurationValueAsync(id, ct);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateProductValueJson(
        [FromBody] UpdateProductValueJsonInput input, CancellationToken ct)
    {
        if (input is null || input.Id <= 0)
            return Json(new { success = false, message = "Gecersiz istek." });
        try
        {
            await _logisticsConfigurationService.UpdateProductConfigurationValueAsync(input.Id, input.Description, input.Aciklama, ct);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    public sealed record UpdateProductValueJsonInput(int Id, string? Description, string? Aciklama);

    public sealed record SaveProductValueJsonInput(
        int FeatureId,
        string? Description,
        string? TextValue,
        decimal? NumericValue,
        DateTime? DateValue,
        string? Aciklama = null);

    // â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
    // ProductFeatureEdit Phase 3 - Ozellik stok baglama (JSON)
    // â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

    /// <summary>
    /// Feature'a bagli stok listesini tamamen yeniden yazar (full replace).
    /// Mevcut SaveProductConfigurationFeatureStocksAsync servisi cagirilir.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SaveProductFeatureStocksJson(
        [FromBody] SaveProductFeatureStocksJsonInput input,
        CancellationToken ct)
    {
        if (input is null || input.FeatureId <= 0)
        {
            return Json(new { success = false, message = "Gecersiz istek." });
        }

        try
        {
            await _logisticsConfigurationService.SaveProductConfigurationFeatureStocksAsync(
                new SaveProductConfigurationFeatureStocksRequest(
                    input.FeatureId,
                    input.StockCodes ?? Array.Empty<string>()),
                ct);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    public sealed record SaveProductFeatureStocksJsonInput(
        int FeatureId,
        IReadOnlyCollection<string>? StockCodes);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProductFeature(
        [Bind(Prefix = "FeatureInput")] ProductFeatureInput featureInput,
        string? search,
        int? page,
        int? pageSize,
        int? selectedFeatureId,
        int? selectedValueId,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse(featureInput.DataType, true, out ConfigurationFieldDataType dataType) ||
            !Enum.IsDefined(dataType))
        {
            ModelState.AddModelError(nameof(featureInput.DataType), "Gecerli bir veri tipi seciniz.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildProductConfigurationViewModelAsync(
                featureInput,
                new ProductValueInput(),
                new ProductConfigInput(),
                ProductConfigurationTabs.Feature,
                search,
                page,
                pageSize,
                selectedFeatureId,
                selectedValueId,
                null,
                cancellationToken);
            return View(nameof(ProductConfiguration), invalidModel);
        }

        try
        {
            var createdFeatureId = await _logisticsConfigurationService.CreateProductConfigurationFeatureAsync(
                new CreateProductConfigurationFeatureRequest(
                    featureInput.Name,
                    dataType,
                    featureInput.IsActive,
                    featureInput.UnitOfMeasure),
                cancellationToken);

            TempData["AdminSuccess"] = "Ozellik tanimi kaydedildi.";
            return RedirectToAction(
                nameof(ProductConfiguration),
                new
                {
                    tab = ProductConfigurationTabs.Feature,
                    search,
                    page,
                    pageSize,
                    selectedFeatureId = createdFeatureId,
                    selectedValueId
                });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildProductConfigurationViewModelAsync(
                featureInput,
                new ProductValueInput(),
                new ProductConfigInput(),
                ProductConfigurationTabs.Feature,
                search,
                page,
                pageSize,
                selectedFeatureId,
                selectedValueId,
                null,
                cancellationToken);
            return View(nameof(ProductConfiguration), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProductValue(
        [Bind(Prefix = "ValueInput")] ProductValueInput valueInput,
        string? search,
        int? page,
        int? pageSize,
        int? selectedFeatureId,
        int? selectedValueId,
        CancellationToken cancellationToken)
    {
        var resolvedFeatureId = valueInput.FeatureId ?? selectedFeatureId;
        if (!valueInput.FeatureId.HasValue && resolvedFeatureId.HasValue)
        {
            valueInput.FeatureId = resolvedFeatureId;
        }

        var isAjax = Request.Headers.XRequestedWith == "XMLHttpRequest";

        if (!ModelState.IsValid)
        {
            if (isAjax)
                return BadRequest(new { error = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)) });

            var invalidModel = await BuildProductConfigurationViewModelAsync(
                new ProductFeatureInput
                {
                    DataType = ConfigurationFieldDataType.Text.ToString()
                },
                valueInput,
                new ProductConfigInput(),
                ProductConfigurationTabs.Value,
                search,
                page,
                pageSize,
                resolvedFeatureId,
                selectedValueId,
                null,
                cancellationToken);
            return View(nameof(ProductConfiguration), invalidModel);
        }

        try
        {
            var (newId, newCode) = await _logisticsConfigurationService.CreateProductConfigurationValueAsync(
                new CreateProductConfigurationValueRequest(
                    valueInput.FeatureId!.Value,
                    valueInput.Description ?? string.Empty,
                    valueInput.TextValue,
                    valueInput.NumericValue,
                    valueInput.DateValue,
                    valueInput.IsActive),
                cancellationToken);

            if (isAjax)
                return Ok(new { id = newId, code = newCode, description = valueInput.Description ?? string.Empty });

            TempData["AdminSuccess"] = "Ozellige deger tanimi kaydedildi.";
            return RedirectToAction(
                nameof(ProductConfiguration),
                new
                {
                    tab = ProductConfigurationTabs.Value,
                    search,
                    page,
                    pageSize,
                    selectedFeatureId = valueInput.FeatureId
                });
        }
        catch (ArgumentException ex)
        {
            if (isAjax)
                return BadRequest(new { error = ex.Message });

            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildProductConfigurationViewModelAsync(
                new ProductFeatureInput
                {
                    DataType = ConfigurationFieldDataType.Text.ToString()
                },
                valueInput,
                new ProductConfigInput(),
                ProductConfigurationTabs.Value,
                search,
                page,
                pageSize,
                resolvedFeatureId,
                selectedValueId,
                null,
                cancellationToken);
            return View(nameof(ProductConfiguration), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProductConfig(
        [Bind(Prefix = "ConfigInput")] ProductConfigInput configInput,
        string? search,
        int? page,
        int? pageSize,
        int? selectedFeatureId,
        int? selectedValueId,
        CancellationToken cancellationToken)
    {
        var resolvedFeatureId = configInput.FeatureId ?? selectedFeatureId;
        var resolvedValueId = configInput.ValueId ?? selectedValueId;

        if (!configInput.FeatureId.HasValue && resolvedFeatureId.HasValue)
        {
            configInput.FeatureId = resolvedFeatureId;
        }

        if (!configInput.ValueId.HasValue && resolvedValueId.HasValue)
        {
            configInput.ValueId = resolvedValueId;
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildProductConfigurationViewModelAsync(
                new ProductFeatureInput
                {
                    DataType = ConfigurationFieldDataType.Text.ToString()
                },
                new ProductValueInput(),
                configInput,
                ProductConfigurationTabs.Config,
                search,
                page,
                pageSize,
                resolvedFeatureId,
                resolvedValueId,
                null,
                cancellationToken);
            return View(nameof(ProductConfiguration), invalidModel);
        }

        try
        {
            await _logisticsConfigurationService.CreateProductConfigurationItemAsync(
                new CreateProductConfigurationItemRequest(
                    configInput.RelatedMaterialCode,
                    configInput.FeatureId!.Value,
                    configInput.ValueId!.Value,
                    configInput.IsActive),
                cancellationToken);

            TempData["AdminSuccess"] = "Urun konfigurasyonu kaydedildi.";
            return RedirectToAction(
                nameof(ProductConfiguration),
                new
                {
                    tab = ProductConfigurationTabs.Config,
                    search,
                    page,
                    pageSize,
                    selectedFeatureId = configInput.FeatureId,
                    selectedValueId = configInput.ValueId
                });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildProductConfigurationViewModelAsync(
                new ProductFeatureInput
                {
                    DataType = ConfigurationFieldDataType.Text.ToString()
                },
                new ProductValueInput(),
                configInput,
                ProductConfigurationTabs.Config,
                search,
                page,
                pageSize,
                resolvedFeatureId,
                resolvedValueId,
                null,
                cancellationToken);
            return View(nameof(ProductConfiguration), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProductFeature(
        [Bind(Prefix = "EditInput")] FeatureEditInput editInput,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse(editInput.DataType, true, out ConfigurationFieldDataType editDataType) ||
            !Enum.IsDefined(editDataType))
        {
            ModelState.AddModelError(nameof(editInput.DataType), "Gecerli bir veri tipi seciniz.");
        }

        if (!ModelState.IsValid)
        {
            TempData["AdminError"] = "Ozellik bilgileri gecersiz.";
            return RedirectToAction(nameof(ProductConfiguration));
        }

        try
        {
            await _logisticsConfigurationService.UpdateProductConfigurationFeatureAsync(
                new UpdateProductConfigurationFeatureRequest(editInput.Id, editInput.Name, editDataType, editInput.UnitOfMeasure),
                cancellationToken);

            TempData["AdminSuccess"] = "Ozellik guncellendi.";
        }
        catch (ArgumentException ex)
        {
            TempData["AdminError"] = ex.Message;
        }

        return RedirectToAction(nameof(ProductConfiguration));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProductFeature(
        int id,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            await _logisticsConfigurationService.DeleteProductConfigurationFeatureAsync(id, cancellationToken);
            TempData["AdminSuccess"] = "Ozellik kaydi silindi.";
            return RedirectToAction(nameof(ProductConfiguration), new
            {
                tab = ProductConfigurationTabs.Feature,
                search,
                page,
                pageSize
            });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildProductConfigurationViewModelAsync(
                new ProductFeatureInput
                {
                    DataType = ConfigurationFieldDataType.Text.ToString()
                },
                new ProductValueInput(),
                new ProductConfigInput(),
                ProductConfigurationTabs.Feature,
                search,
                page,
                pageSize,
                id,
                null,
                null,
                cancellationToken);
            return View(nameof(ProductConfiguration), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProductFeatureStocks(
        int? featureId,
        string[]? selectedStockCodes,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!featureId.HasValue || featureId.Value <= 0)
        {
            ModelState.AddModelError(nameof(featureId), "Stok eslestirmesi icin bir ozellik secilmelidir.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildProductConfigurationViewModelAsync(
                new ProductFeatureInput
                {
                    DataType = ConfigurationFieldDataType.Text.ToString()
                },
                new ProductValueInput(),
                new ProductConfigInput(),
                ProductConfigurationTabs.Feature,
                search,
                page,
                pageSize,
                featureId ?? 0,
                null,
                null,
                cancellationToken);
            return View(nameof(ProductConfiguration), invalidModel);
        }

        try
        {
            await _logisticsConfigurationService.SaveProductConfigurationFeatureStocksAsync(
                new SaveProductConfigurationFeatureStocksRequest(
                    featureId ?? 0,
                    selectedStockCodes ?? Array.Empty<string>()),
                cancellationToken);

            TempData["AdminSuccess"] = "Ozellige gecerli stok eslestirmesi kaydedildi.";
            return RedirectToAction(
                nameof(ProductConfiguration),
                new
                {
                    tab = ProductConfigurationTabs.Feature,
                    search,
                    page,
                    pageSize,
                    selectedFeatureId = featureId
                });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildProductConfigurationViewModelAsync(
                new ProductFeatureInput
                {
                    DataType = ConfigurationFieldDataType.Text.ToString()
                },
                new ProductValueInput(),
                new ProductConfigInput(),
                ProductConfigurationTabs.Feature,
                search,
                page,
                pageSize,
                featureId,
                null,
                null,
                cancellationToken);
            return View(nameof(ProductConfiguration), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProductValue(
        int id,
        string? search,
        int? page,
        int? pageSize,
        int? selectedFeatureId,
        CancellationToken cancellationToken)
    {
        var isAjax = Request.Headers.ContainsKey("X-Requested-With") &&
                     string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        try
        {
            await _logisticsConfigurationService.DeleteProductConfigurationValueAsync(id, cancellationToken);
            if (isAjax) return Ok(new { success = true });
            TempData["AdminSuccess"] = "Deger kaydi silindi.";
            return RedirectToAction(
                nameof(ProductConfiguration),
                new
                {
                    tab = ProductConfigurationTabs.Value,
                    search,
                    page,
                    pageSize,
                    selectedFeatureId
                });
        }
        catch (ArgumentException ex)
        {
            if (isAjax) return BadRequest(new { error = ex.Message });
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildProductConfigurationViewModelAsync(
                new ProductFeatureInput
                {
                    DataType = ConfigurationFieldDataType.Text.ToString()
                },
                new ProductValueInput
                {
                    FeatureId = selectedFeatureId
                },
                new ProductConfigInput
                {
                    FeatureId = selectedFeatureId
                },
                ProductConfigurationTabs.Value,
                search,
                page,
                pageSize,
                selectedFeatureId,
                id,
                null,
                cancellationToken);
            return View(nameof(ProductConfiguration), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProductConfig(
        int id,
        string? search,
        int? page,
        int? pageSize,
        int? selectedFeatureId,
        int? selectedValueId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _logisticsConfigurationService.DeleteProductConfigurationItemAsync(id, cancellationToken);
            TempData["AdminSuccess"] = "Yapilandirma kaydi silindi.";
            return RedirectToAction(
                nameof(ProductConfiguration),
                new
                {
                    tab = ProductConfigurationTabs.Config,
                    search,
                    page,
                    pageSize,
                    selectedFeatureId,
                    selectedValueId
                });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildProductConfigurationViewModelAsync(
                new ProductFeatureInput
                {
                    DataType = ConfigurationFieldDataType.Text.ToString()
                },
                new ProductValueInput
                {
                    FeatureId = selectedFeatureId
                },
                new ProductConfigInput
                {
                    FeatureId = selectedFeatureId,
                    ValueId = selectedValueId
                },
                ProductConfigurationTabs.Config,
                search,
                page,
                pageSize,
                selectedFeatureId,
                selectedValueId,
                id,
                cancellationToken);
            return View(nameof(ProductConfiguration), invalidModel);
        }
    }

    /// <summary>
    /// Stok kodlarÄ± listesi - React Kombinasyon ekranÄ± iÃ§in.
    /// YalnÄ±zca hem Ã¶zellik baÄŸlantÄ±sÄ± olan hem de stok kartÄ± bulunan kodlar dÃ¶ndÃ¼rÃ¼lÃ¼r;
    /// bÃ¶ylece kombinasyon kaydetme sÄ±rasÄ±nda "malzeme kodu bulunamadÄ±" hatasÄ± oluÅŸmaz.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> StockCodesJson(CancellationToken cancellationToken)
    {
        var snapshot      = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(cancellationToken);
        var stockSnapshot = await _logisticsConfigurationService.GetSnapshotAsync(cancellationToken);

        // Stok kartÄ± indeksi - hÄ±zlÄ± arama iÃ§in
        var cardIndex = stockSnapshot.Items
            .GroupBy(s => s.MaterialCode.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var codes = snapshot.FeatureStockLinks
            .Select(x => x.StockCode.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(code => cardIndex.ContainsKey(code))   // stok kartÄ± olmayanlarÄ± filtrele
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(code =>
            {
                var card = cardIndex[code];
                return new
                {
                    value = code,
                    label = $"{code} \u2014 {card.MaterialName}"
                };
            })
            .ToArray();

        return Json(codes);
    }

    /// <summary>
    /// Stoka ait Ã¶zellikler + deÄŸerler + mevcut kombinasyonlar - React Kombinasyon Matrisi ekranÄ± iÃ§in.
    /// Cross-product hesaplanmaz; mevcut DB kayÄ±tlarÄ± doÄŸrudan dÃ¶ndÃ¼rÃ¼lÃ¼r. Client tarafÄ± cross-product Ã¼retir.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> CombinationsDataJson(
        string stockCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
            return Json(new { features = Array.Empty<object>(), combos = Array.Empty<object>() });

        stockCode = stockCode.Trim().ToUpperInvariant();

        var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(cancellationToken);

        var linkedFeatureIds = snapshot.FeatureStockLinks
            .Where(x => string.Equals(x.StockCode, stockCode, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.FeatureId)
            .ToHashSet();

        var featureVms = snapshot.Features
            .Where(f => linkedFeatureIds.Contains(f.Id) && f.IsActive)
            .OrderBy(f => f.Code, StringComparer.OrdinalIgnoreCase)
            .Select(f => new CombinationFeatureVm
            {
                Id       = f.Id,
                Code     = f.Code,
                Name     = f.Name,
                DataType = f.DataType,
                Values   = snapshot.Values
                    .Where(v => v.FeatureId == f.Id && v.IsActive)
                    .OrderBy(v => v.Code, StringComparer.OrdinalIgnoreCase)
                    .Select(v => new CombinationValueVm { Id = v.Id, Code = v.Code, Description = v.Description })
                    .ToArray()
            })
            .ToList();

        var featuresResult = featureVms.Select(f => new
        {
            id     = f.Id,
            code   = f.Code,
            name   = f.Name,
            values = f.Values.Select(v => new { id = v.Id, code = v.Code, description = v.Description }).ToArray()
        }).ToArray();

        // Mevcut kombinasyonlarÄ± doÄŸrudan dÃ¶ndÃ¼r (cross-product client-side Ã¼retilir)
        var existingConfigs = snapshot.Configurations
            .Where(c => string.Equals(c.RelatedMaterialCode, stockCode, StringComparison.OrdinalIgnoreCase)
                     && c.ValueIds != null && c.ValueIds.Count > 0)
            .ToList();

        // valueId â†’ value bilgisi iÃ§in hÄ±zlÄ± eriÅŸim haritasÄ±
        var valueIndex = featureVms
            .SelectMany(f => f.Values.Select(v => new { f.Id, f.Name, v }))
            .ToDictionary(x => x.v.Id, x => new { featureId = x.Id, featureName = x.Name, x.v.Code, x.v.Description });

        var combosResult = existingConfigs
            .Select(config =>
            {
                var valueIdSet = config.ValueIds.ToHashSet();
                var cells = featureVms
                    .Select(f =>
                    {
                        var matchedValue = f.Values.FirstOrDefault(v => valueIdSet.Contains(v.Id));
                        if (matchedValue == null) return (object?)null;
                        return (object)new
                        {
                            featureId        = f.Id,
                            featureName      = f.Name,
                            valueId          = matchedValue.Id,
                            valueCode        = matchedValue.Code,
                            valueDescription = matchedValue.Description
                        };
                    })
                    .Where(c => c != null)
                    .ToArray();
                return new
                {
                    id          = config.Id,
                    code        = config.ConfigCode,
                    description = config.ConfigName,
                    date        = config.CreatedDate.ToString("dd.MM.yyyy"),
                    cells
                };
            })
            .Where(c => c.cells.Length > 0)
            .ToArray();

        return Json(new { features = featuresResult, combos = combosResult });
    }

    [HttpGet]
    public async Task<IActionResult> ProductCombinations(
        string? stockCode,
        CancellationToken cancellationToken)
    {
        var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(cancellationToken);
        var stockSnapshot = await _logisticsConfigurationService.GetSnapshotAsync(cancellationToken);

        var allStockCodes = stockSnapshot.Items
            .Where(x => x.TrackCombinations)
            .Select(x => x.MaterialCode)
            .Concat(snapshot.FeatureStockLinks.Select(x => x.StockCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var resolvedStockCode = string.IsNullOrWhiteSpace(stockCode)
            ? null
            : stockCode.Trim().ToUpperInvariant();

        var stockCodeOptions = allStockCodes
            .Select(code => 
            {
                var st = stockSnapshot.Items.FirstOrDefault(s => string.Equals(s.MaterialCode, code, StringComparison.OrdinalIgnoreCase));
                var textLabel = st != null ? $"{code} - {st.MaterialName}" : code;
                return new SelectListItem(textLabel, code, string.Equals(code, resolvedStockCode, StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();

        var linkedFeatures = new List<CombinationFeatureVm>();
        var combinations = new List<CombinationRowVm>();

        if (!string.IsNullOrWhiteSpace(resolvedStockCode))
        {
            var linkedFeatureIds = snapshot.FeatureStockLinks
                .Where(x => string.Equals(x.StockCode, resolvedStockCode, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.FeatureId)
                .ToHashSet();

            var existingConfigs = snapshot.Configurations
                .Where(c => string.Equals(c.RelatedMaterialCode, resolvedStockCode, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var linkedValueIds = existingConfigs
                .SelectMany(c => c.ValueIds ?? Array.Empty<int>())
                .Concat(existingConfigs.Where(c => c.ValueId.HasValue).Select(c => c.ValueId!.Value))
                .ToHashSet();

            linkedFeatures = snapshot.Features
                .Where(x => linkedFeatureIds.Contains(x.Id) && x.IsActive)
                .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .Select(x => new CombinationFeatureVm
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    DataType = x.DataType,
                    Values = snapshot.Values
                        .Where(v => v.FeatureId == x.Id && v.IsActive)
                        .OrderBy(v => v.Code, StringComparer.OrdinalIgnoreCase)
                        .Select(v => new CombinationValueVm
                        {
                            Id = v.Id,
                            Code = v.Code,
                            Description = v.Description,
                            Value = v.Value,
                            IsSelected = linkedValueIds.Contains(v.Id)
                        })
                        .ToArray()
                })
                .ToList();

            if (linkedFeatures.Count > 0 && linkedFeatures.All(f => f.Values.Count > 0))
            {
                combinations = BuildCombinations(resolvedStockCode, linkedFeatures, existingConfigs);
            }
        }

        var selectedItem = resolvedStockCode != null
            ? stockSnapshot.Items.FirstOrDefault(x => string.Equals(x.MaterialCode, resolvedStockCode, StringComparison.OrdinalIgnoreCase))
            : null;

        return View(new ProductCombinationsViewModel
        {
            StockCodeOptions = stockCodeOptions,
            SelectedStockCode = resolvedStockCode,
            SelectedStockId = selectedItem?.Id,
            SelectedStockName = selectedItem?.MaterialName,
            LinkedFeatures = linkedFeatures,
            Combinations = combinations
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProductCombinations(
        [FromForm] string stockCode,
        string[]? selectedCombinations,
        [FromForm] string? workspace,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
            return RedirectToAction(nameof(ProductCombinations), new { workspace });

        var resolvedStockCode = stockCode.Contains(',') ? stockCode.Split(',')[0].Trim() : stockCode.Trim();

        var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(cancellationToken);
        
        var requiredFeatureIds = snapshot.FeatureStockLinks
            .Where(x => string.Equals(x.StockCode, resolvedStockCode, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.FeatureId)
            .ToHashSet();

        var activeRequiredFeatureIds = snapshot.Features
            .Where(f => requiredFeatureIds.Contains(f.Id) && f.IsActive)
            .Select(f => f.Id)
            .ToHashSet();

        var selectedValueIds = (selectedCombinations ?? Array.Empty<string>())
            .Select(x => int.TryParse(x, out var id) ? id : 0)
            .Where(x => x > 0)
            .ToHashSet();

        var selectedFeatureIds = snapshot.Values
            .Where(v => selectedValueIds.Contains(v.Id))
            .Select(v => v.FeatureId)
            .ToHashSet();

        if (activeRequiredFeatureIds.Count > 0 && selectedFeatureIds.Count < activeRequiredFeatureIds.Count)
        {
            TempData["AdminError"] = "Stoga bagli ozelliklerin tamamindan en az birer deger secmelisiniz (Eksik secim yapildi). Kayit islemi iptal edildi.";
            return RedirectToAction(nameof(ProductCombinations), new { stockCode = resolvedStockCode, workspace });
        }

        var existingConfigs = snapshot.Configurations
            .Where(x => string.Equals(x.RelatedMaterialCode, resolvedStockCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // TÃ¼m eski konfigurasyonlari sil (yeniden uretecegiz)
        foreach (var exist in existingConfigs)
        {
            await _logisticsConfigurationService.DeleteProductConfigurationItemAsync(exist.Id, cancellationToken);
        }

        // Yeni kombinasyon satirlarindan olusan coklu kombinasyonu yarat
        if (selectedCombinations != null && selectedCombinations.Length > 0)
        {
            var selectedValuesByFeature = snapshot.Values
                .Where(v => selectedValueIds.Contains(v.Id))
                .GroupBy(v => v.FeatureId)
                .ToList();

            if (selectedValuesByFeature.Count > 0)
            {
                var permutations = new List<List<int>> { new List<int>() };
                foreach (var featureGroup in selectedValuesByFeature)
                {
                    var newPermutations = new List<List<int>>();
                    foreach (var existing in permutations)
                    {
                        foreach (var val in featureGroup)
                        {
                            var combo = new List<int>(existing) { val.Id };
                            newPermutations.Add(combo);
                        }
                    }
                    permutations = newPermutations;
                }

                foreach (var comboIds in permutations)
                {
                    if (comboIds.Count > 0)
                    {
                        await _logisticsConfigurationService.CreateProductConfigurationCombinationAsync(
                            new CreateProductConfigurationCombinationRequest(resolvedStockCode, comboIds.ToArray(), true),
                            cancellationToken);
                    }
                }
            }
        }

        TempData["AdminSuccess"] = "Kombinasyon secimleri basariyla kaydedildi.";

        if (!string.IsNullOrWhiteSpace(workspace))
        {
            return RedirectToAction(nameof(ProductCombinations), new { stockCode = resolvedStockCode, workspace });
        }
        return RedirectToAction(nameof(ProductCombinations), new { stockCode = resolvedStockCode });
    }

    [HttpPost]
    public async Task<IActionResult> SaveProductCombinationsJson(
        [FromBody] SaveProductCombinationsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.StockCode))
            return Json(new { success = false, message = "Stok kodu bos olamaz." });

        var resolvedStockCode = request.StockCode.Contains(',')
            ? request.StockCode.Split(',')[0].Trim()
            : request.StockCode.Trim();

        var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(cancellationToken);

        var requiredFeatureIds = snapshot.FeatureStockLinks
            .Where(x => string.Equals(x.StockCode, resolvedStockCode, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.FeatureId)
            .ToHashSet();

        var activeRequiredFeatureIds = snapshot.Features
            .Where(f => requiredFeatureIds.Contains(f.Id) && f.IsActive)
            .Select(f => f.Id)
            .ToHashSet();

        var selectedValueIds = (request.SelectedCombinations ?? Array.Empty<string>())
            .Select(x => int.TryParse(x, out var id) ? id : 0)
            .Where(x => x > 0)
            .ToHashSet();

        var selectedFeatureIds = snapshot.Values
            .Where(v => selectedValueIds.Contains(v.Id))
            .Select(v => v.FeatureId)
            .ToHashSet();

        if (activeRequiredFeatureIds.Count > 0 && selectedFeatureIds.Count < activeRequiredFeatureIds.Count)
            return Json(new { success = false, message = "Stoga bagli ozelliklerin tamamindan en az birer deger secmelisiniz." });

        var existingConfigs = snapshot.Configurations
            .Where(x => string.Equals(x.RelatedMaterialCode, resolvedStockCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var exist in existingConfigs)
            await _logisticsConfigurationService.DeleteProductConfigurationItemAsync(exist.Id, cancellationToken);

        if (selectedValueIds.Count > 0)
        {
            var selectedValuesByFeature = snapshot.Values
                .Where(v => selectedValueIds.Contains(v.Id))
                .GroupBy(v => v.FeatureId)
                .ToList();

            if (selectedValuesByFeature.Count > 0)
            {
                var permutations = new List<List<int>> { new List<int>() };
                foreach (var featureGroup in selectedValuesByFeature)
                {
                    var newPermutations = new List<List<int>>();
                    foreach (var existing in permutations)
                        foreach (var val in featureGroup)
                            newPermutations.Add(new List<int>(existing) { val.Id });
                    permutations = newPermutations;
                }

                foreach (var comboIds in permutations)
                    if (comboIds.Count > 0)
                        await _logisticsConfigurationService.CreateProductConfigurationCombinationAsync(
                            new CreateProductConfigurationCombinationRequest(resolvedStockCode, comboIds.ToArray(), true),
                            cancellationToken);
            }
        }

        return Json(new { success = true, message = "Kombinasyon secimleri basariyla kaydedildi." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProductCombination(int id, CancellationToken cancellationToken)
    {
        await _logisticsConfigurationService.DeleteProductConfigurationItemAsync(id, cancellationToken);
        return Json(new { success = true });
    }

    public sealed record UpdateCombinationDescriptionRequest(int Id, string? Description);

    [HttpPost]
    public async Task<IActionResult> UpdateCombinationDescriptionJson(
        [FromBody] UpdateCombinationDescriptionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _logisticsConfigurationService.UpdateProductCombinationDescriptionAsync(
                request.Id, request.Description, cancellationToken);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    public sealed record AddSingleCombinationRequest(string StockCode, int[] ValueIds);

    [HttpPost]
    public async Task<IActionResult> AddSingleCombinationJson(
        [FromBody] AddSingleCombinationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.StockCode))
                return Json(new { success = false, message = "Stok kodu bos olamaz." });
            if (request.ValueIds == null || request.ValueIds.Length == 0)
                return Json(new { success = false, message = "En az bir deger secilmelidir." });

            var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(cancellationToken);
            var stockCode = request.StockCode.Trim().ToUpperInvariant();

            var requiredFeatureIds = snapshot.FeatureStockLinks
                .Where(x => string.Equals(x.StockCode, stockCode, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.FeatureId)
                .ToHashSet();

            var activeFeatureIds = snapshot.Features
                .Where(f => requiredFeatureIds.Contains(f.Id) && f.IsActive)
                .Select(f => f.Id)
                .ToHashSet();

            var selectedValueIds = request.ValueIds.Where(v => v > 0).ToHashSet();

            var coveredFeatureIds = snapshot.Values
                .Where(v => selectedValueIds.Contains(v.Id))
                .Select(v => v.FeatureId)
                .ToHashSet();

            if (activeFeatureIds.Count > 0 && !activeFeatureIds.All(f => coveredFeatureIds.Contains(f)))
                return Json(new { success = false, message = "Her ozellik icin bir deger secilmelidir." });

            // Ayni kombinasyon zaten var mi kontrol et
            var existing = snapshot.Configurations
                .Where(c => string.Equals(c.RelatedMaterialCode, stockCode, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var selectedSorted = selectedValueIds.OrderBy(x => x).ToArray();
            var isDuplicate = existing.Any(c =>
            {
                var ids = (c.ValueIds ?? Array.Empty<int>()).OrderBy(x => x).ToArray();
                return ids.SequenceEqual(selectedSorted);
            });

            if (isDuplicate)
                return Json(new { success = false, message = "Bu kombinasyon zaten mevcut." });

            var (id, code) = await _logisticsConfigurationService.CreateProductConfigurationCombinationAsync(
                new CreateProductConfigurationCombinationRequest(stockCode, selectedValueIds.ToArray(), true),
                cancellationToken);

            // Olusturulan kombinasyonun degerlerini donus icin hazirla
            var cells = snapshot.Values
                .Where(v => selectedValueIds.Contains(v.Id))
                .Select(v => new { featureName = v.FeatureName, valueDesc = v.Description, valueCode = v.Code })
                .ToArray();

            return Json(new { success = true, id, code, cells });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    private static List<CombinationRowVm> BuildCombinations(
        string stockCode,
        IReadOnlyList<CombinationFeatureVm> features,
        IReadOnlyList<ProductConfigurationItemDto> existingConfigs)
    {
        var rows = new List<List<CombinationCellVm>> { new() };

        foreach (var feature in features)
        {
            var expanded = new List<List<CombinationCellVm>>();
            foreach (var existing in rows)
            {
                foreach (var val in feature.Values)
                {
                    var newRow = new List<CombinationCellVm>(existing)
                    {
                        new CombinationCellVm
                        {
                            FeatureId = feature.Id,
                            FeatureCode = feature.Code,
                            FeatureName = feature.Name,
                            ValueId = val.Id,
                            ValueCode = val.Code,
                            ValueDescription = val.Description
                        }
                    };
                    expanded.Add(newRow);
                }
            }
            rows = expanded;
        }

        return rows.Select((cells, idx) =>
        {
            var cellValueIds = cells.Select(c => c.ValueId).OrderBy(id => id).ToList();
            var matchingConfig = existingConfigs.FirstOrDefault(config =>
            {
                if (config.ValueIds == null || config.ValueIds.Count == 0) return false;
                var configValueIds = config.ValueIds.OrderBy(id => id).ToList();
                return configValueIds.SequenceEqual(cellValueIds);
            });

            return new CombinationRowVm
            {
                Id = matchingConfig?.Id ?? 0,
                Index = idx + 1,
                Cells = cells,
                CombinedCode = matchingConfig?.ConfigCode ?? (stockCode + "-" + string.Join("-", cells.Select(c => c.ValueCode))),
                IsSelected = matchingConfig != null
            };
        }).ToList();
    }

    private async Task<ProductConfigurationViewModel> BuildProductConfigurationViewModelAsync(
        ProductFeatureInput featureInput,
        ProductValueInput valueInput,
        ProductConfigInput configInput,
        string activeTab,
        string? search,
        int? page,
        int? pageSize,
        int? selectedFeatureId,
        int? selectedValueId,
        int? selectedConfigId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(featureInput.DataType))
        {
            featureInput.DataType = ConfigurationFieldDataType.Text.ToString();
        }

        var normalizedActiveTab = NormalizeProductConfigurationTab(activeTab);
        var normalizedSearch = search?.Trim() ?? string.Empty;
        var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(cancellationToken);
        var stockSnapshot = await _logisticsConfigurationService.GetSnapshotAsync(cancellationToken);

        var selectedConfig = selectedConfigId.HasValue
            ? snapshot.Configurations.FirstOrDefault(x => x.Id == selectedConfigId.Value)
            : null;

        var resolvedFeatureId = valueInput.FeatureId ?? configInput.FeatureId ?? selectedFeatureId;
        var resolvedValueId = configInput.ValueId ?? selectedValueId;

        if (selectedConfig is not null)
        {
            resolvedFeatureId ??= selectedConfig.FeatureId;
            resolvedValueId ??= selectedConfig.ValueId;
        }

        if (!valueInput.FeatureId.HasValue && resolvedFeatureId.HasValue)
        {
            valueInput.FeatureId = resolvedFeatureId;
        }

        if (!configInput.FeatureId.HasValue && resolvedFeatureId.HasValue)
        {
            configInput.FeatureId = resolvedFeatureId;
        }

        var availableValues = snapshot.Values
            .Where(x => !resolvedFeatureId.HasValue || x.FeatureId == resolvedFeatureId.Value)
            .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (resolvedValueId.HasValue && availableValues.All(x => x.Id != resolvedValueId.Value))
        {
            resolvedValueId = null;
            configInput.ValueId = null;
        }
        else if (!configInput.ValueId.HasValue && resolvedValueId.HasValue)
        {
            configInput.ValueId = resolvedValueId;
        }

        if (selectedConfig is not null &&
            (selectedConfig.FeatureId != resolvedFeatureId || selectedConfig.ValueId != resolvedValueId))
        {
            selectedConfig = null;
        }

        var dataTypeOptions = Enum.GetValues<ConfigurationFieldDataType>()
            .Select(x => new SelectListItem(
                GetDataTypeLabel(x),
                x.ToString(),
                string.Equals(featureInput.DataType, x.ToString(), StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var featureOptions = snapshot.Features
            .Where(x => x.IsActive)
            .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x => new SelectListItem(
                $"{x.Code} - {x.Name} ({x.DataType})",
                x.Id.ToString(),
                resolvedFeatureId == x.Id))
            .ToArray();

        var valueOptions = availableValues
            .Select(x => new SelectListItem(
                $"{x.Code} - {x.Description} ({x.Value})",
                x.Id.ToString(),
                resolvedValueId == x.Id))
            .ToArray();

        var selectedFeatureStockCodes = snapshot.FeatureStockLinks
            .Where(x => x.FeatureId == resolvedFeatureId)
            .Select(x => x.StockCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var featureStockOptions = stockSnapshot.Items
            .Where(x => x.IsActive && x.TrackCombinations)
            .OrderBy(x => x.MaterialCode, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ProductFeatureStockOptionViewModel
            {
                StockCode = x.MaterialCode,
                StockName = x.MaterialName,
                IsSelected = selectedFeatureStockCodes.Contains(x.MaterialCode)
            })
            .ToArray();

        var materialCodeOptions = stockSnapshot.Items
            .Where(x => x.IsActive)
            .OrderBy(x => x.MaterialCode, StringComparer.OrdinalIgnoreCase)
            .Select(x => new SelectListItem(
                $"{x.MaterialCode} - {x.MaterialName}",
                x.MaterialCode,
                string.Equals(configInput.RelatedMaterialCode, x.MaterialCode, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var featureGridSource = snapshot.Features
            .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Where(x =>
                string.IsNullOrWhiteSpace(normalizedSearch) ||
                ContainsInsensitive(x.Code, normalizedSearch) ||
                ContainsInsensitive(x.Name, normalizedSearch) ||
                ContainsInsensitive(x.DataType, normalizedSearch))
            .ToArray();
        var featurePageSize = await ResolveGridPageSizeAsync(
            "logistics-product-features",
            string.Equals(normalizedActiveTab, ProductConfigurationTabs.Feature, StringComparison.Ordinal)
                ? pageSize
                : null,
            cancellationToken);
        var featureTotalCount = featureGridSource.Length;
        var featureTotalPages = featureTotalCount == 0 ? 0 : (int)Math.Ceiling(featureTotalCount / (double)featurePageSize);
        var featurePage = string.Equals(normalizedActiveTab, ProductConfigurationTabs.Feature, StringComparison.Ordinal)
            ? (featureTotalPages == 0 ? 1 : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), featureTotalPages))
            : 1;
        var gridFeatures = featureGridSource
            .Skip((featurePage - 1) * featurePageSize)
            .Take(featurePageSize)
            .ToArray();

        var valueGridSource = snapshot.Values
            .Where(x => !resolvedFeatureId.HasValue || x.FeatureId == resolvedFeatureId.Value)
            .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Description ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Where(x =>
                string.IsNullOrWhiteSpace(normalizedSearch) ||
                ContainsInsensitive(x.FeatureCode, normalizedSearch) ||
                ContainsInsensitive(x.FeatureName, normalizedSearch) ||
                ContainsInsensitive(x.Code, normalizedSearch) ||
                ContainsInsensitive(x.Description ?? string.Empty, normalizedSearch) ||
                ContainsInsensitive(x.Value ?? string.Empty, normalizedSearch))
            .ToArray();
        var valuePageSize = await ResolveGridPageSizeAsync(
            "logistics-product-values",
            string.Equals(normalizedActiveTab, ProductConfigurationTabs.Value, StringComparison.Ordinal)
                ? pageSize
                : null,
            cancellationToken);
        var valueTotalCount = valueGridSource.Length;
        var valueTotalPages = valueTotalCount == 0 ? 0 : (int)Math.Ceiling(valueTotalCount / (double)valuePageSize);
        var valuePage = string.Equals(normalizedActiveTab, ProductConfigurationTabs.Value, StringComparison.Ordinal)
            ? (valueTotalPages == 0 ? 1 : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), valueTotalPages))
            : 1;
        var gridValues = valueGridSource
            .Skip((valuePage - 1) * valuePageSize)
            .Take(valuePageSize)
            .ToArray();

        var configurationGridSource = snapshot.Configurations
            .OrderBy(x => x.ConfigCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RelatedMaterialCode, StringComparer.OrdinalIgnoreCase)
            .Where(x =>
                string.IsNullOrWhiteSpace(normalizedSearch) ||
                ContainsInsensitive(x.ConfigCode, normalizedSearch) ||
                ContainsInsensitive(x.RelatedMaterialCode, normalizedSearch) ||
                ContainsInsensitive(x.FeatureCode, normalizedSearch) ||
                ContainsInsensitive(x.FeatureName, normalizedSearch) ||
                ContainsInsensitive(x.ValueCode, normalizedSearch) ||
                ContainsInsensitive(x.ValueDescription, normalizedSearch) ||
                ContainsInsensitive(x.Value ?? string.Empty, normalizedSearch))
            .ToArray();
        var configurationPageSize = await ResolveGridPageSizeAsync(
            "logistics-product-configurations",
            string.Equals(normalizedActiveTab, ProductConfigurationTabs.Config, StringComparison.Ordinal)
                ? pageSize
                : null,
            cancellationToken);
        var configurationTotalCount = configurationGridSource.Length;
        var configurationTotalPages = configurationTotalCount == 0
            ? 0
            : (int)Math.Ceiling(configurationTotalCount / (double)configurationPageSize);
        var configurationPage = string.Equals(normalizedActiveTab, ProductConfigurationTabs.Config, StringComparison.Ordinal)
            ? (configurationTotalPages == 0
                ? 1
                : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), configurationTotalPages))
            : 1;
        var gridConfigurations = configurationGridSource
            .Skip((configurationPage - 1) * configurationPageSize)
            .Take(configurationPageSize)
            .ToArray();

        var featureValueCounts = snapshot.Values
            .GroupBy(x => x.FeatureId)
            .ToDictionary(g => g.Key, g => g.Count());
        var featureStockLinkLookup = snapshot.FeatureStockLinks
            .GroupBy(x => x.FeatureId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.StockCode).ToArray());
        var valuesByFeature = snapshot.Values
            .GroupBy(x => x.FeatureId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<FeatureValueItemViewModel>)g
                .OrderBy(v => v.Code, StringComparer.OrdinalIgnoreCase)
                .Select(v => new FeatureValueItemViewModel
                {
                    Id = v.Id,
                    Code = v.Code,
                    Description = v.Description,
                    Value = v.Value
                })
                .ToArray());
        var featureRows = snapshot.Features
            .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ProductFeatureRowViewModel
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                DataType = x.DataType switch
                {
                    "NUMBER" => "Numeric",
                    "DATE" => "Date",
                    _ => "Text"
                },
                UnitOfMeasure = x.UnitOfMeasure,
                IsActive = x.IsActive,
                ValueCount = featureValueCounts.GetValueOrDefault(x.Id, 0),
                LinkedStockCodes = featureStockLinkLookup.TryGetValue(x.Id, out var stocks)
                    ? stocks
                    : Array.Empty<string>(),
                Values = valuesByFeature.TryGetValue(x.Id, out var vals)
                    ? vals
                    : Array.Empty<FeatureValueItemViewModel>()
            })
            .ToArray();

        var measureUnits = await _logisticsConfigurationService.GetUnitsAsync(cancellationToken);
        var unitOfMeasureOptions = measureUnits
            .Where(x => x.IsActive)
            .Select(x => new SelectListItem($"{x.UnitCode} - {x.UnitName}", x.UnitCode))
            .ToArray();

        return new ProductConfigurationViewModel
        {
            Features = snapshot.Features,
            Values = snapshot.Values,
            Configurations = snapshot.Configurations,
            GridFeatures = gridFeatures,
            GridValues = gridValues,
            GridConfigurations = gridConfigurations,
            DataTypeOptions = dataTypeOptions,
            UnitOfMeasureOptions = unitOfMeasureOptions,
            FeatureOptions = featureOptions,
            ValueOptions = valueOptions,
            MaterialCodeOptions = materialCodeOptions,
            FeatureStockOptions = featureStockOptions,
            FeatureRows = featureRows,
            FeaturesListState = BuildGridListState(
                gridKey: "logistics-product-features",
                searchTerm: string.Equals(normalizedActiveTab, ProductConfigurationTabs.Feature, StringComparison.Ordinal)
                    ? normalizedSearch
                    : string.Empty,
                page: featurePage,
                pageSize: featurePageSize,
                totalCount: featureTotalCount,
                totalPages: featureTotalPages,
                itemLabel: "ozellik"),
            ValuesListState = BuildGridListState(
                gridKey: "logistics-product-values",
                searchTerm: string.Equals(normalizedActiveTab, ProductConfigurationTabs.Value, StringComparison.Ordinal)
                    ? normalizedSearch
                    : string.Empty,
                page: valuePage,
                pageSize: valuePageSize,
                totalCount: valueTotalCount,
                totalPages: valueTotalPages,
                itemLabel: "deger"),
            ConfigurationsListState = BuildGridListState(
                gridKey: "logistics-product-configurations",
                searchTerm: string.Equals(normalizedActiveTab, ProductConfigurationTabs.Config, StringComparison.Ordinal)
                    ? normalizedSearch
                    : string.Empty,
                page: configurationPage,
                pageSize: configurationPageSize,
                totalCount: configurationTotalCount,
                totalPages: configurationTotalPages,
                itemLabel: "yapilandirma"),
            ActiveTab = normalizedActiveTab,
            SelectedFeatureId = resolvedFeatureId,
            SelectedValueId = resolvedValueId,
            SelectedConfigId = selectedConfig?.Id,
            FeatureInput = featureInput,
            ValueInput = valueInput,
            ConfigInput = configInput
        };
    }


    private async Task<LocationsViewModel> BuildLocationsViewModelAsync(
        LocationInput input,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var locations = await _logisticsConfigurationService.GetLocationsAsync(cancellationToken);
        var layout = await _uiConfigurationService.GetScreenDesignLayoutAsync(
            ScreenDesignCatalog.LocationsScreenCode,
            cancellationToken);
        var lookup = locations.ToDictionary(x => x.Id);
        var normalizedSearch = search?.Trim() ?? string.Empty;

        var locationTypeOptions = BuildLocationTypeOptions(input.LocationTypeCode);
        var parentOptions = locations
            .Where(x => !input.Id.HasValue || x.Id != input.Id.Value)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.LocationCode, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var label = string.IsNullOrWhiteSpace(x.LocationName)
                    ? x.LocationCode
                    : $"{x.LocationCode} - {x.LocationName}";
                return new SelectListItem(label, x.Id.ToString(), input.ParentId == x.Id);
            })
            .ToArray();

        var filteredLocations = locations
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.LocationCode, StringComparer.OrdinalIgnoreCase)
            .Where(x =>
                string.IsNullOrWhiteSpace(normalizedSearch) ||
                ContainsInsensitive(GetLocationTypeDisplayName(x.LocationTypeCode), normalizedSearch) ||
                ContainsInsensitive(x.LocationCode, normalizedSearch) ||
                ContainsInsensitive(x.LocationName ?? string.Empty, normalizedSearch) ||
                ContainsInsensitive(
                    x.ParentId.HasValue && lookup.TryGetValue(x.ParentId.Value, out var parentLocation)
                        ? string.IsNullOrWhiteSpace(parentLocation.LocationName)
                            ? parentLocation.LocationCode
                            : $"{parentLocation.LocationCode} {parentLocation.LocationName}"
                        : string.Empty,
                    normalizedSearch))
            .Select(x =>
            {
                var parent = x.ParentId.HasValue ? lookup.GetValueOrDefault(x.ParentId.Value) : null;
                return new LocationRowViewModel
                {
                    Id = x.Id,
                    ParentId = x.ParentId,
                    ParentLocationCode = parent?.LocationCode,
                    ParentLocationDisplayName = parent is null
                        ? "-"
                        : string.IsNullOrWhiteSpace(parent.LocationName)
                            ? parent.LocationCode
                            : $"{parent.LocationCode} - {parent.LocationName}",
                    LocationTypeCode = x.LocationTypeCode,
                    LocationTypeDisplayName = GetLocationTypeDisplayName(x.LocationTypeCode),
                    LocationCode = x.LocationCode,
                    LocationName = x.LocationName,
                    SortOrder = x.SortOrder,
                    MaxWeightCapacity = x.MaxWeightCapacity,
                    VolumeCapacity = x.VolumeCapacity,
                    IsActive = x.IsActive
                };
            })
            .ToArray();
        var resolvedPageSize = await ResolveGridPageSizeAsync("logistics-warehouse-locations", pageSize, cancellationToken);
        var totalCount = filteredLocations.Length;
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);
        var currentPage = totalPages == 0 ? 1 : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), totalPages);
        var rows = filteredLocations
            .Skip((currentPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToArray();

        return new LocationsViewModel
        {
            Locations = rows,
            LocationTypeOptions = locationTypeOptions,
            ParentLocationOptions = parentOptions,
            LayoutTabs = BuildScreenLayoutTabs(layout),
            ListState = BuildGridListState(
                gridKey: "logistics-warehouse-locations",
                searchTerm: normalizedSearch,
                page: currentPage,
                pageSize: resolvedPageSize,
                totalCount: totalCount,
                totalPages: totalPages,
                itemLabel: "lokasyon"),
            LocationInput = input
        };
    }


    private async Task<UnitsViewModel> BuildUnitsViewModelAsync(
        UnitInput input,
        string? search,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var definitions = await _logisticsConfigurationService.GetUnitsAsync(cancellationToken);
        var layout = await _uiConfigurationService.GetScreenDesignLayoutAsync(
            ScreenDesignCatalog.UnitsScreenCode,
            cancellationToken);
        var normalizedSearch = search?.Trim() ?? string.Empty;
        var filteredDefinitions = definitions
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.UnitCode, StringComparer.OrdinalIgnoreCase)
            .Where(x =>
                string.IsNullOrWhiteSpace(normalizedSearch) ||
                ContainsInsensitive(x.UnitCode, normalizedSearch) ||
                ContainsInsensitive(x.UnitName, normalizedSearch))
            .Select(x => new UnitRowViewModel
            {
                Id = x.Id,
                UnitCode = x.UnitCode,
                UnitName = x.UnitName,
                SortOrder = x.SortOrder,
                IsActive = x.IsActive
            })
            .ToArray();
        var resolvedPageSize = await ResolveGridPageSizeAsync("logistics-measure-units", pageSize, cancellationToken);
        var totalCount = filteredDefinitions.Length;
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);
        var currentPage = totalPages == 0 ? 1 : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), totalPages);
        var rows = filteredDefinitions
            .Skip((currentPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToArray();

        return new UnitsViewModel
        {
            Definitions = rows,
            LayoutTabs = BuildScreenLayoutTabs(layout),
            ListState = BuildGridListState(
                gridKey: "logistics-measure-units",
                searchTerm: normalizedSearch,
                page: currentPage,
                pageSize: resolvedPageSize,
                totalCount: totalCount,
                totalPages: totalPages,
                itemLabel: "olcu birimi"),
            Input = input
        };
    }

    private async Task<MaterialCardsViewModel> BuildMaterialCardsViewModelAsync(
        MaterialCardCreateInput stockInput,
        MaterialCardListQuery listQuery,
        CancellationToken cancellationToken)
    {
        var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(cancellationToken);

        var allMaterialCards = snapshot.Items
            .Select(x => new MaterialCardRowViewModel
            {
                Id = x.Id,
                MaterialCode = x.MaterialCode,
                MaterialName = x.MaterialName,
                MaterialDescription = x.MaterialDescription,
                MaterialTypeId = x.MaterialTypeId,
                IsActive = x.IsActive
            })
            .ToArray();

        var filteredMaterialCards = ApplyMaterialCardListQuery(allMaterialCards, listQuery);
        var totalCount = filteredMaterialCards.Count;
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)listQuery.PageSize);
        var currentPage = totalPages == 0
            ? 1
            : Math.Min(listQuery.Page, totalPages);
        var materialCards = filteredMaterialCards
            .Skip((currentPage - 1) * listQuery.PageSize)
            .Take(listQuery.PageSize)
            .ToArray();

        var combinations = new List<MaterialCardCombinationViewModel>();
        if (!string.IsNullOrWhiteSpace(stockInput.MaterialCode))
        {
            var productSnapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(cancellationToken);
            
            var stockCombinations = productSnapshot.Configurations
                .Where(x => string.Equals(x.RelatedMaterialCode, stockInput.MaterialCode, StringComparison.OrdinalIgnoreCase) 
                         && x.ValueIds != null 
                         && x.ValueIds.Any())
                .ToList();
                
            foreach (var c in stockCombinations)
            {
                var valNames = productSnapshot.Values
                    .Where(v => c.ValueIds.Contains(v.Id))
                    .OrderBy(v => v.FeatureId)
                    .Select(v => $"{v.FeatureName}: {v.Description ?? v.Code}")
                    .ToList();
                    
                combinations.Add(new MaterialCardCombinationViewModel
                {
                    Id = c.Id,
                    CombinationCode = c.ConfigCode ?? string.Empty,
                    CombinationName = valNames.Count > 0 ? string.Join(" | ", valNames) : (c.ConfigName ?? string.Empty)
                });
            }
        }

        return new MaterialCardsViewModel
        {
            MaterialCards = materialCards,
            MaterialCardLookup = allMaterialCards
                .Select(x => new MaterialCardLookupViewModel
                {
                    Id = x.Id,
                    MaterialCode = x.MaterialCode,
                    EditUrl = Url.Action(
                        nameof(MaterialCards),
                        "Logistics",
                        BuildMaterialCardListRouteValuesForUrl(x.Id, listQuery)) ?? string.Empty
                })
                .ToArray(),
            ListState = new MaterialCardListStateViewModel
            {
                GridKey = "logistics-material-cards",
                SearchTerm = listQuery.SearchTerm,
                SortBy = listQuery.SortBy,
                SortDirection = listQuery.SortDirection,
                Page = currentPage,
                PageSize = listQuery.PageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                ItemLabel = "malzeme karti",
                PageSizeOptions = BuildPageSizeOptions(listQuery.PageSize)
            },
            StockInput = stockInput,
            SelectedMeta = stockInput.ItemId.HasValue && stockInput.ItemId.Value != 0
                ? snapshot.Items
                    .Where(x => x.Id == stockInput.ItemId.Value)
                    .Select(x => new MaterialCardMetaViewModel
                    {
                        CreatedByUserId = x.CreatedByUserId,
                        CreatedDate = x.CreatedDate,
                        ModifiedByUserId = x.ModifiedByUserId,
                        ModifiedDate = x.ModifiedDate
                    })
                    .FirstOrDefault()
                : null,
            Combinations = combinations,
            MeasureUnits = (await _logisticsConfigurationService.GetUnitsAsync(cancellationToken))
                .Where(u => u.IsActive)
                .Select(u => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(u.UnitName, u.UnitCode))
                .ToList(),
            SupplierAccounts = (await _financeService.GetContactsAsync(null, null, cancellationToken))
                .Where(a => a.IsActive)
                .Select(a => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem($"{a.AccountCode} - {a.AccountTitle}", a.AccountCode))
                .ToList(),
            AvailableColumns = MaterialCardGridColumns,
            VisibleColumns = await GetMaterialCardVisibleColumnsAsync(cancellationToken)
        };
    }

    private RouteValueDictionary BuildMaterialCardListRouteValuesForUrl(int? id, MaterialCardListQuery listQuery)
    {
        var values = new RouteValueDictionary();
        if (id.HasValue && id.Value != 0)
        {
            values["id"] = id.Value;
        }

        if (!string.IsNullOrWhiteSpace(listQuery.SearchTerm))
        {
            values["search"] = listQuery.SearchTerm;
        }

        values["sortBy"] = listQuery.SortBy;
        values["sortDirection"] = listQuery.SortDirection;
        values["page"] = listQuery.Page;
        values["pageSize"] = listQuery.PageSize;

        if (IsWorkspaceFrameRequest(Request))
        {
            values["workspace"] = "1";
        }

        return values;
    }

    private static IReadOnlyCollection<MaterialCardRowViewModel> ApplyMaterialCardListQuery(
        IReadOnlyCollection<MaterialCardRowViewModel> materialCards,
        MaterialCardListQuery listQuery)
    {
        IEnumerable<MaterialCardRowViewModel> query = materialCards;
        if (!string.IsNullOrWhiteSpace(listQuery.SearchTerm))
        {
            var searchTerm = listQuery.SearchTerm.Trim();
            query = query.Where(x => MatchesMaterialCardSearch(x, searchTerm));
        }

        query = (listQuery.SortBy, listQuery.SortDirection) switch
        {
            (MaterialCardListSortOptions.MaterialName, "desc") => query
                .OrderByDescending(x => x.MaterialName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.MaterialCode, StringComparer.OrdinalIgnoreCase),
            (MaterialCardListSortOptions.MaterialName, _) => query
                .OrderBy(x => x.MaterialName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.MaterialCode, StringComparer.OrdinalIgnoreCase),
            (_, "desc") => query
                .OrderByDescending(x => x.MaterialCode, StringComparer.OrdinalIgnoreCase),
            _ => query
                .OrderBy(x => x.MaterialCode, StringComparer.OrdinalIgnoreCase)
        };

        return query.ToArray();
    }

    private static bool MatchesMaterialCardSearch(MaterialCardRowViewModel row, string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return true;
        }

        var searchBlob = string.Join(
            ' ',
            new[] { row.MaterialCode, row.MaterialName, row.MaterialDescription }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        return searchBlob.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<ScreenLayoutTabViewModel> BuildScreenLayoutTabs(ScreenDesignLayoutDto layout)
    {
        var visibleItems = layout.Items
            .Where(x => x.IsVisible)
            .ToLookup(x => x.TabKey, StringComparer.OrdinalIgnoreCase);

        return layout.Tabs
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.TabLabel, StringComparer.OrdinalIgnoreCase)
            .Select(tab => new ScreenLayoutTabViewModel
            {
                TabKey = tab.TabKey,
                TabLabel = tab.TabLabel,
                DisplayOrder = tab.DisplayOrder,
                Items = visibleItems[tab.TabKey]
                    .OrderBy(x => x.DisplayOrder)
                    .ThenBy(x => x.ItemLabel, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new ScreenLayoutItemViewModel
                    {
                        ItemKey = x.ItemKey,
                        ItemLabel = x.ItemLabel,
                        DisplayOrder = x.DisplayOrder,
                        ColumnSpan = x.ColumnSpan,
                        IsVisible = x.IsVisible,
                        IsRequired = x.IsRequired
                    })
                    .ToArray()
            })
            .ToArray();
    }

    private async Task<MaterialCardCreateInput> BuildMaterialCardInputAsync(int? stockCardId, CancellationToken cancellationToken)
    {
        if (!stockCardId.HasValue || stockCardId.Value == 0)
        {
            return new MaterialCardCreateInput();
        }

        var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(cancellationToken);
        var stockCard = snapshot.Items.FirstOrDefault(x => x.Id == stockCardId.Value);
        if (stockCard is null)
        {
            return new MaterialCardCreateInput();
        }

        return new MaterialCardCreateInput
        {
            ItemId = stockCard.Id,
            MaterialCode = stockCard.MaterialCode,
            MaterialName = stockCard.MaterialName,
            MaterialDescription = stockCard.MaterialDescription,
            MaterialTypeId = stockCard.MaterialTypeId,
            TrackCombinations = stockCard.TrackCombinations,
            ExistingImageUrl = stockCard.ImageData != null && stockCard.ImageMimeType != null 
                ? $"data:{stockCard.ImageMimeType};base64,{Convert.ToBase64String(stockCard.ImageData)}" 
                : null
        };
    }


    private static Guid IntToGuid(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    private static string GetDataTypeLabel(ConfigurationFieldDataType dataType) =>
        dataType switch
        {
            ConfigurationFieldDataType.Text => "Metin",
            ConfigurationFieldDataType.Numeric => "Sayisal",
            ConfigurationFieldDataType.Date => "Tarih",
            _ => dataType.ToString()
        };

    private static string NormalizeProductConfigurationTab(string? tab)
    {
        if (string.Equals(tab, ProductConfigurationTabs.Value, StringComparison.OrdinalIgnoreCase))
        {
            return ProductConfigurationTabs.Value;
        }

        if (string.Equals(tab, ProductConfigurationTabs.Config, StringComparison.OrdinalIgnoreCase))
        {
            return ProductConfigurationTabs.Config;
        }

        return ProductConfigurationTabs.Feature;
    }

    private static IReadOnlyCollection<SelectListItem> BuildLocationTypeOptions(string? selectedValue)
    {
        var normalizedSelectedValue = NormalizeLocationTypeCode(selectedValue);

        return
        [
            new SelectListItem("Fabrika", "FACTORY", string.Equals(normalizedSelectedValue, "FACTORY", StringComparison.OrdinalIgnoreCase)),
            new SelectListItem("Bolum", "SECTION", string.Equals(normalizedSelectedValue, "SECTION", StringComparison.OrdinalIgnoreCase)),
            new SelectListItem("Raf", "SHELF", string.Equals(normalizedSelectedValue, "SHELF", StringComparison.OrdinalIgnoreCase)),
            new SelectListItem("Hucre", "BIN", string.Equals(normalizedSelectedValue, "BIN", StringComparison.OrdinalIgnoreCase))
        ];
    }

    private static string GetLocationTypeDisplayName(string? locationTypeCode)
    {
        if (string.Equals(locationTypeCode, "FACTORY", StringComparison.OrdinalIgnoreCase))
        {
            return "Fabrika";
        }

        if (string.Equals(locationTypeCode, "SECTION", StringComparison.OrdinalIgnoreCase))
        {
            return "Bolum";
        }

        if (string.Equals(locationTypeCode, "SHELF", StringComparison.OrdinalIgnoreCase))
        {
            return "Raf";
        }

        if (string.Equals(locationTypeCode, "BIN", StringComparison.OrdinalIgnoreCase))
        {
            return "Hucre";
        }

        return locationTypeCode ?? "-";
    }

    private static string NormalizeLocationTypeCode(string? locationTypeCode)
    {
        if (string.Equals(locationTypeCode, "AISLE", StringComparison.OrdinalIgnoreCase))
        {
            return "SECTION";
        }

        return locationTypeCode ?? "SECTION";
    }

    private async Task<IReadOnlyCollection<SelectListItem>> BuildUnitOfMeasureOptionsAsync(
        string? selectedUnitOfMeasure,
        CancellationToken cancellationToken)
    {
        var definitions = await _logisticsConfigurationService.GetUnitsAsync(cancellationToken);
        var options = definitions
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.UnitCode, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var text = string.Equals(x.UnitCode, x.UnitName, StringComparison.OrdinalIgnoreCase)
                    ? x.UnitCode
                    : $"{x.UnitCode} - {x.UnitName}";
                return new SelectListItem(text, x.UnitCode);
            })
            .ToList();

        var trimmed = selectedUnitOfMeasure?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) &&
            !options.Any(x => string.Equals(x.Value, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            options.Insert(0, new SelectListItem(trimmed, trimmed));
        }

        return options;
    }


    private static bool IsAjaxRequest(HttpRequest request)
    {
        return string.Equals(
            request.Headers["X-Requested-With"],
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);
    }

    private RedirectToActionResult RedirectToMaterialCards(object? routeValues = null)
    {
        var values = new RouteValueDictionary(routeValues);
        CopyMaterialCardListRouteValues(values, Request.Query);
        if (IsWorkspaceFrameRequest(Request))
        {
            values["workspace"] = "1";
        }

        return RedirectToAction(nameof(MaterialCards), values);
    }

    private async Task<int> ResolveGridPageSizeAsync(
        string gridKey,
        int? requestedPageSize,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var storedPageSize = await _uiConfigurationService.GetGridPageSizePreferenceAsync(
            userId,
            gridKey,
            20,
            cancellationToken);

        var resolvedPageSize = requestedPageSize.GetValueOrDefault() > 0
            ? requestedPageSize!.Value
            : storedPageSize;

        if (userId.HasValue && userId.Value != Guid.Empty && resolvedPageSize != storedPageSize)
        {
            await _uiConfigurationService.SaveGridPageSizePreferenceAsync(
                userId.Value,
                gridKey,
                resolvedPageSize,
                cancellationToken);
        }

        return resolvedPageSize;
    }

    private static GridListStateViewModel BuildGridListState(
        string gridKey,
        string searchTerm,
        int page,
        int pageSize,
        int totalCount,
        int totalPages,
        string itemLabel) =>
        new()
        {
            GridKey = gridKey,
            SearchTerm = searchTerm,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            ItemLabel = itemLabel,
            PageSizeOptions = BuildPageSizeOptions(pageSize)
        };

    private static IReadOnlyCollection<SelectListItem> BuildPageSizeOptions(int pageSize) =>
    [
        new SelectListItem("10", "10", pageSize == 10),
        new SelectListItem("20", "20", pageSize == 20),
        new SelectListItem("30", "30", pageSize == 30),
        new SelectListItem("50", "50", pageSize == 50),
        new SelectListItem("100", "100", pageSize == 100)
    ];

    private Guid? GetCurrentUserId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawUserId, out var userId) ? userId : null;
    }

    private static bool ContainsInsensitive(string source, string value) =>
        source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static void CopyMaterialCardListRouteValues(RouteValueDictionary values, IQueryCollection query)
    {
        CopyMaterialCardListRouteValue(values, query, "search");
        CopyMaterialCardListRouteValue(values, query, "sortBy");
        CopyMaterialCardListRouteValue(values, query, "sortDirection");
        CopyMaterialCardListRouteValue(values, query, "page");
        CopyMaterialCardListRouteValue(values, query, "pageSize");
    }

    private static void CopyMaterialCardListRouteValue(
        RouteValueDictionary values,
        IQueryCollection query,
        string key)
    {
        if (values.ContainsKey(key))
        {
            return;
        }

        var value = query[key].ToString();
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value;
        }
    }

    private async Task<MaterialCardListQuery> GetCurrentMaterialCardListQueryAsync(CancellationToken cancellationToken)
    {
        var resolvedPageSize = await ResolveGridPageSizeAsync(
            "logistics-material-cards",
            int.TryParse(Request.Query["pageSize"], out var pageSize) ? pageSize : null,
            cancellationToken);

        return NormalizeMaterialCardListQuery(
            Request.Query["search"],
            Request.Query["sortBy"],
            Request.Query["sortDirection"],
            int.TryParse(Request.Query["page"], out var page) ? page : null,
            resolvedPageSize);
    }

    private static MaterialCardListQuery NormalizeMaterialCardListQuery(
        string? search,
        string? sortBy,
        string? sortDirection,
        int? page,
        int pageSize)
    {
        var normalizedSortBy = sortBy switch
        {
            MaterialCardListSortOptions.MaterialName => MaterialCardListSortOptions.MaterialName,
            _ => MaterialCardListSortOptions.MaterialCode
        };

        return new MaterialCardListQuery(
            SearchTerm: search?.Trim() ?? string.Empty,
            SortBy: normalizedSortBy,
            SortDirection: string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc",
            Page: page.GetValueOrDefault() > 0 ? page!.Value : 1,
            PageSize: pageSize > 0 ? pageSize : 20);
    }

    private static bool IsWorkspaceFrameRequest(HttpRequest request)
    {
        return string.Equals(request.Query["workspace"], "1", StringComparison.Ordinal);
    }

    private sealed record MaterialCardListQuery(
        string SearchTerm,
        string SortBy,
        string SortDirection,
        int Page,
        int PageSize);

    private static string ToMaterialMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Malzeme karti islemi tamamlanamadi.";
        }

        return message
            .Replace("Stok karti", "Malzeme karti")
            .Replace("Stok", "Malzeme")
            .Replace("stok", "malzeme");
    }

    // â"€â"€ Grid kolon yonetimi â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

    private static readonly GridColumnDefinition[] MaterialCardGridColumns =
    [
        new() { Key = "MaterialCode",        Label = "Malzeme Kodu" },
        new() { Key = "MaterialName",        Label = "Malzeme Adi" },
        new() { Key = "MaterialDescription", Label = "Aciklama" },
        new() { Key = "UnitName",            Label = "Olcu Birimi" },
        new() { Key = "IsActive",            Label = "Durum" },
        new() { Key = "CreatedDate",         Label = "Kayit Tarihi" },
        new() { Key = "ModifiedDate",        Label = "Guncelleme Tarihi" },
    ];

    private static readonly string[] DefaultMaterialCardColumns =
        ["MaterialCode", "MaterialName", "MaterialDescription"];

    private async Task<IReadOnlyCollection<string>> GetMaterialCardVisibleColumnsAsync(CancellationToken ct)
    {
        var userId = GetUserId();
        var cols = await _uiConfigurationService.GetGridColumnPreferencesAsync(userId, "logistics-material-cards", ct);
        return cols.Count > 0 ? cols : DefaultMaterialCardColumns;
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    // â"€â"€ AJAX JSON Endpoint'leri (MaterialCards) â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

    [HttpGet]
    public async Task<IActionResult> GetMaterialCards(
        string? search,
        string? sortBy,
        string? sortDirection,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var resolvedPageSize = await ResolveGridPageSizeAsync("logistics-material-cards", pageSize, ct);
        var listQuery = NormalizeMaterialCardListQuery(search, sortBy, sortDirection, page, resolvedPageSize);

        var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(ct);
        var allRows = snapshot.Items
            .Select(x => new MaterialCardRowViewModel
            {
                Id = x.Id,
                MaterialCode = x.MaterialCode,
                MaterialName = x.MaterialName,
                MaterialDescription = x.MaterialDescription,
                MaterialTypeId = x.MaterialTypeId,
                IsActive = x.IsActive
            })
            .ToArray();

        var filtered = ApplyMaterialCardListQuery(allRows, listQuery);
        var totalCount = filtered.Count;
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)listQuery.PageSize);
        var currentPage = totalPages == 0 ? 1 : Math.Min(listQuery.Page, totalPages);
        var items = filtered.Skip((currentPage - 1) * listQuery.PageSize).Take(listQuery.PageSize).ToArray();

        var visibleColumns = await GetMaterialCardVisibleColumnsAsync(ct);

        return Json(new
        {
            items = items.Select(i => new
            {
                i.Id,
                i.MaterialCode,
                i.MaterialName,
                i.MaterialDescription,
                unitName = (string?)null,
                i.IsActive,
                i.MaterialTypeId
            }),
            totalCount,
            totalPages,
            page = currentPage,
            pageSize = listQuery.PageSize,
            visibleColumns
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetMaterialCard(int id, CancellationToken ct)
    {
        var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(ct);
        var card = snapshot.Items.FirstOrDefault(x => x.Id == id);
        if (card is null) return NotFound();

        string? imageUrl = card.ImageData != null && card.ImageMimeType != null
            ? $"data:{card.ImageMimeType};base64,{Convert.ToBase64String(card.ImageData)}"
            : null;

        // Kombinasyonlar
        var combinations = new List<object>();
        if (card.TrackCombinations)
        {
            var productSnapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(ct);
            var stockCombinations = productSnapshot.Configurations
                .Where(x => string.Equals(x.RelatedMaterialCode, card.MaterialCode, StringComparison.OrdinalIgnoreCase)
                          && x.ValueIds != null && x.ValueIds.Any())
                .ToList();
            foreach (var c in stockCombinations)
            {
                var valNames = productSnapshot.Values
                    .Where(v => c.ValueIds.Contains(v.Id))
                    .OrderBy(v => v.FeatureId)
                    .Select(v => $"{v.FeatureName}: {v.Description ?? v.Code}")
                    .ToList();
                combinations.Add(new
                {
                    id = c.Id,
                    combinationCode = c.ConfigCode ?? string.Empty,
                    combinationName = valNames.Count > 0 ? string.Join(" | ", valNames) : (c.ConfigName ?? string.Empty)
                });
            }
        }

        return Json(new
        {
            stockCardId = card.Id,
            materialCode = card.MaterialCode,
            materialName = card.MaterialName,
            materialDescription = card.MaterialDescription,
            materialTypeId = card.MaterialTypeId,
            trackCombinations = card.TrackCombinations,
            existingImageUrl = imageUrl,
            meta = new
            {
                createdDate = card.CreatedDate,
                createdByUserId = card.CreatedByUserId,
                modifiedDate = card.ModifiedDate,
                modifiedByUserId = card.ModifiedByUserId
            },
            combinations
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveMaterialCardJson(
        [FromBody] SaveMaterialCardJsonInput input,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.MaterialCode) || string.IsNullOrWhiteSpace(input.MaterialName))
            return Json(new { success = false, message = "Malzeme kodu ve adi bos olamaz." });

        try
        {
            // Ayni kodla mevcut kart varsa guncellemeye yonlendir
            if (!input.ItemId.HasValue || input.ItemId.Value == 0)
            {
                var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(ct);
                var existing = snapshot.Items.FirstOrDefault(x =>
                    string.Equals(x.MaterialCode, input.MaterialCode.Trim(), StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                    input.ItemId = existing.Id;
            }

            var isUpdate = input.ItemId.HasValue && input.ItemId.Value != 0;

            // Resim islemleri
            byte[]? imageData = null;
            string? imageMimeType = null;
            if (string.Equals(input.ProductImageBase64, "CLEAR", StringComparison.OrdinalIgnoreCase))
            {
                imageData = Array.Empty<byte>();
                imageMimeType = "CLEAR";
            }
            else if (!string.IsNullOrEmpty(input.ProductImageBase64) && input.ProductImageBase64.Contains(","))
            {
                var base64Data = input.ProductImageBase64.Substring(input.ProductImageBase64.IndexOf(",") + 1);
                imageData = Convert.FromBase64String(base64Data);
                imageMimeType = "image/jpeg";
            }

            var companyId = GetCompanyId();
            var placeholders = new Dictionary<string, string>
            {
                ["EntityId"] = (input.ItemId ?? 0).ToString(),
                ["UserName"] = User.FindFirstValue(ClaimTypes.Name) ?? "system",
                ["MaterialCode"] = input.MaterialCode ?? "",
                ["MaterialName"] = input.MaterialName ?? "",
                ["MaterialDescription"] = input.MaterialDescription ?? "",
                ["MaterialTypeId"] = input.MaterialTypeId?.ToString() ?? "",
                ["IsActive"] = "1"
            };

            if (isUpdate)
            {
                placeholders["EntityId"] = input.ItemId!.Value.ToString();
                await _integrationEventService.ExecuteBeforeEventAsync(companyId, "Item", "BeforeUpdate", placeholders, ct);

                await _logisticsConfigurationService.UpdateItemAsync(
                    new UpdateItemRequest(
                        ItemId: input.ItemId!.Value,
                        MaterialCode: input.MaterialCode,
                        MaterialName: input.MaterialName,
                        MaterialDescription: input.MaterialDescription,
                        MaterialTypeId: input.MaterialTypeId,
                        TrackCombinations: input.TrackCombinations,
                        ImageData: imageData,
                        ImageMimeType: imageMimeType),
                    ct);

                _integrationEventService.FireAfterEvent(companyId, "Item", "AfterUpdate", placeholders);
            }
            else
            {
                await _integrationEventService.ExecuteBeforeEventAsync(companyId, "Item", "BeforeCreate", placeholders, ct);

                await _logisticsConfigurationService.CreateItemAsync(
                    new CreateItemRequest(
                        MaterialCode: input.MaterialCode,
                        MaterialName: input.MaterialName,
                        MaterialDescription: input.MaterialDescription,
                        MaterialTypeId: input.MaterialTypeId,
                        TrackCombinations: input.TrackCombinations,
                        ImageData: imageData,
                        ImageMimeType: imageMimeType),
                    ct);

                _integrationEventService.FireAfterEvent(companyId, "Item", "AfterCreate", placeholders);
            }

            // Kaydedilen kartin ID'sini dondur
            var savedCardId = input.ItemId;
            if (!isUpdate && (savedCardId == null || savedCardId == 0))
            {
                var refreshed = await _logisticsConfigurationService.GetSnapshotAsync(ct);
                var created = refreshed.Items
                    .FirstOrDefault(x => string.Equals(x.MaterialCode, input.MaterialCode, StringComparison.OrdinalIgnoreCase));
                if (created != null) savedCardId = created.Id;
            }

            return Json(new
            {
                success = true,
                message = isUpdate ? "Malzeme karti guncellendi." : "Malzeme karti kaydedildi.",
                id = savedCardId
            });
        }
        catch (CalibraHub.Application.Services.IntegrationEventException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ToMaterialMessage(ex.Message) });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteMaterialCardJson(int id, CancellationToken ct)
    {
        try
        {
            var companyId = GetCompanyId();
            var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(ct);
            var card = snapshot.Items.FirstOrDefault(x => x.Id == id);
            var placeholders = new Dictionary<string, string>
            {
                ["EntityId"] = id.ToString(),
                ["UserName"] = User.FindFirstValue(ClaimTypes.Name) ?? "system",
                ["MaterialCode"] = card?.MaterialCode ?? "",
                ["MaterialName"] = card?.MaterialName ?? "",
                ["MaterialDescription"] = card?.MaterialDescription ?? "",
                ["MaterialTypeId"] = card?.MaterialTypeId?.ToString() ?? "",
                ["IsActive"] = card?.IsActive == true ? "1" : "0"
            };

            await _integrationEventService.ExecuteBeforeEventAsync(companyId, "Item", "BeforeDelete", placeholders, ct);
            await _logisticsConfigurationService.DeactivateItemAsync(id, ct);
            _integrationEventService.FireAfterEvent(companyId, "Item", "AfterDelete", placeholders);

            return Json(new { success = true, message = "Malzeme karti silindi." });
        }
        catch (CalibraHub.Application.Services.IntegrationEventException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ToMaterialMessage(ex.Message) });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveMaterialCardGridColumns([FromBody] string[] columns, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await _uiConfigurationService.SaveGridColumnPreferencesAsync(userId, "logistics-material-cards", columns, ct);
        return Ok(new { success = true });
    }

    // ── Ölçü Birimi Dönüşümleri (Stok Kartı bazlı) ──

    [HttpGet]
    public async Task<IActionResult> GetStockUnitConversions(int stockCardId, CancellationToken ct)
    {
        var items = await _logisticsConfigurationService.GetStockUnitConversionsAsync(stockCardId, ct);
        var units = await _logisticsConfigurationService.GetUnitsAsync(ct);
        return Json(new
        {
            conversions = items.Select(x => new { x.LineNo, x.UnitCode, x.Multiplier }),
            availableUnits = units.Where(u => u.IsActive)
                .OrderBy(u => u.SortOrder).ThenBy(u => u.UnitCode)
                .Select(u => new { u.UnitCode, u.UnitName }),
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveStockUnitConversions([FromBody] SaveStockUnitConversionsInput input, CancellationToken ct)
    {
        if (input.ItemId <= 0)
            return Json(new { success = false, message = "Stok karti ID gerekli." });

        var items = (input.Items ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.UnitCode) && x.Multiplier > 0)
            .Select(x => new Application.Contracts.SaveStockUnitConversionItem(x.UnitCode.Trim(), x.Multiplier))
            .ToList();

        // Tekrar eden birim kontrolü
        var codes = items.Select(x => x.UnitCode.ToUpperInvariant()).ToList();
        if (codes.Distinct().Count() != codes.Count)
            return Json(new { success = false, message = "Ayni olcu birimi birden fazla tanimlanamaz." });

        await _logisticsConfigurationService.SaveStockUnitConversionsAsync(input.ItemId, items, ct);
        return Json(new { success = true });
    }

    // ── Stok Karti <-> Ozellik Eslestirmesi (Kombinasyon Takibi acik iken) ──

    /// <summary>
    /// Bir stok karti için mevcut tum FEATURE listesini + bu karta bagli olanlari doner.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetStockFeatures(int stockCardId, CancellationToken ct)
    {
        var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(ct);
        var card = snapshot.Items.FirstOrDefault(s => s.Id == stockCardId);
        if (card is null) return NotFound(new { success = false, message = "Stok karti bulunamadi." });

        var productCfg = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(ct);

        var linkedFeatureIds = productCfg.FeatureStockLinks
            .Where(l => string.Equals(l.StockCode, card.MaterialCode.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(l => l.FeatureId)
            .ToHashSet();

        var allFeatures = productCfg.Features
            .Where(f => f.IsActive)
            .OrderBy(f => f.Name)
            .Select(f => new
            {
                id = f.Id,
                code = f.Code,
                name = f.Name,
                dataType = f.DataType,
                unitOfMeasure = f.UnitOfMeasure,
                linked = linkedFeatureIds.Contains(f.Id)
            })
            .ToArray();

        return Json(new
        {
            materialCode = card.MaterialCode,
            features = allFeatures,
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveStockFeatures([FromBody] SaveStockFeaturesInput input, CancellationToken ct)
    {
        if (input is null || input.ItemId <= 0)
            return Json(new { success = false, message = "Stok karti ID gerekli." });

        try
        {
            var snapshot = await _logisticsConfigurationService.GetSnapshotAsync(ct);
            var card = snapshot.Items.FirstOrDefault(s => s.Id == input.ItemId);
            if (card is null) return Json(new { success = false, message = "Stok karti bulunamadi." });

            await _logisticsConfigurationService.SetFeaturesForItemAsync(
                card.MaterialCode,
                input.FeatureIds ?? Array.Empty<int>(),
                ct);

            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Sunucu hatası: " + ex.Message });
        }
    }
}

public sealed record SaveStockUnitConversionsInput(int ItemId, SaveStockUnitConversionLineInput[]? Items);
public sealed record SaveStockUnitConversionLineInput(string UnitCode, decimal Multiplier);
public sealed record SaveStockFeaturesInput(int ItemId, int[]? FeatureIds);

public sealed record SaveProductCombinationsRequest(string StockCode, string[]? SelectedCombinations);
