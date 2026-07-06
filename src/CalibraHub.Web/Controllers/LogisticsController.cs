using CalibraHub.Application.Constants;
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
    private readonly IFinanceService _financeService;
    private readonly IWidgetService _widgetService;
    private readonly IPriceListService _priceListService;
    private readonly ICurrencyService _currencyService;
    private readonly Application.Abstractions.Persistence.ICardGroupRepository _cardGroupRepo;
    private readonly ILogger<LogisticsController> _logger;

    public LogisticsController(
        ILogisticsConfigurationService logisticsConfigurationService,
        IUiConfigurationService uiConfigurationService,
        IFinanceService financeService,
        IWidgetService widgetService,
        IPriceListService priceListService,
        ICurrencyService currencyService,
        Application.Abstractions.Persistence.ICardGroupRepository cardGroupRepo,
        ILogger<LogisticsController> logger)
    {
        _logisticsConfigurationService = logisticsConfigurationService;
        _uiConfigurationService = uiConfigurationService;
        _financeService = financeService;
        _widgetService = widgetService;
        _priceListService = priceListService;
        _currencyService = currencyService;
        _cardGroupRepo = cardGroupRepo;
        _logger = logger;
    }

    private int GetCompanyId()
    {
        var raw = User.FindFirstValue("company_id");
        return int.TryParse(raw, out var id) ? id : 0;
    }


    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MaterialCardEdit)]
    public async Task<IActionResult> MaterialCards(CancellationToken cancellationToken)
    {
        // 2026-05-24: Iframe cache'lenmesi sorununu onle � her zaman fresh HTML.
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
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
        // 2026-05-24: Schema'yi bir kez cek, hem master widget list hem de "admin'in
        // zaten cover ettigi plain field" set'ini cikar � boylece w_kod/w_ad sistem
        // widget'lari admin'in mevcut widget'lariyla cakistirmaz (cift gozukmez).
        var itemsSchema = await _widgetService.GetFormSchemaByCodeAsync("ITEMS", ct);
        // 2026-05-24: Multi-select filter alanlari:
        //   - 5 ayri Grup slotu (Grup 1..5) � her birinin kendi MaterialGroups kategorisi
        //   - Olcu Birimi � Units tanim listesi
        //   - Her aktif Ozellik (ItemFeature) � kendi degerleri (FeatureValue) ile ayri widget
        var allMatGroups = await _logisticsConfigurationService.GetMaterialGroupsAsync(null, ct);
        var groupsByCat = allMatGroups.GroupBy(g => g.GroupCategory).ToDictionary(g => g.Key, g => g.ToList());
        var allUnits = await _logisticsConfigurationService.GetUnitsAsync(ct);
        var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(ct);
        var activeFeatures = snapshot.Features.Where(f => f.IsActive).OrderBy(f => f.Name).ToList();
        var valuesByFeature = snapshot.Values
            .Where(v => v.IsActive)
            .GroupBy(v => v.FeatureId)
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.Description).ToList());

        var handledColumns = ExtractHandledEntityColumns(itemsSchema);

        var masterWidgets = BuildItemsMasterWidgetsFromSchema(itemsSchema);
        // 2026-05-24: System.Text.Json List<object> + heterojen anonymous type sorunu �
        // bazi properties dropped olabiliyor. Dictionary kullanarak her zaman tum key'lerin
        // serialize edildigini garantiliyoruz.

        const string STD_GROUP = "standardalanlar";
        const string STD_LBL   = "Standart Alanlar";
        const string GRP_GROUP = "gruplamalar";
        const string GRP_LBL   = "Gruplamalar";
        const string FEAT_GROUP = "ozellikler";
        const string FEAT_LBL   = "�zellikler ve Kombinasyonlar";

        Dictionary<string, object?> MakeWidget(
            string id, string label, string dataType,
            string group, string groupLabel,
            IReadOnlyList<object>? options = null)
        {
            var d = new Dictionary<string, object?>
            {
                ["id"]           = id,
                ["dbId"]         = (int?)null,
                ["isPlainField"] = false,
                ["type"]         = "data",
                ["dataType"]     = dataType,
                ["label"]        = label,
                ["source"]       = "standard",
                ["group"]        = group,
                ["groupLabel"]   = groupLabel,
            };
            if (options != null) d["options"] = options;
            return d;
        }

        // Sistem alanlari � "Standart Alanlar" grubunda collapsible.
        // w_kod / w_ad sadece admin ITEMS formuna mapleyen widget tanimlanmamissa eklenir.
        if (!handledColumns.Contains("Code"))
            masterWidgets.Add(MakeWidget("w_kod", "Stok Kodu", "text", STD_GROUP, STD_LBL));
        if (!handledColumns.Contains("Name"))
            masterWidgets.Add(MakeWidget("w_ad", "Stok Adi", "text", STD_GROUP, STD_LBL));
        masterWidgets.Add(MakeWidget("w_aktif",       "Durum",              "boolean", STD_GROUP, STD_LBL));
        masterWidgets.Add(MakeWidget("w_kombinasyon", "Kombinasyon Takibi", "boolean", STD_GROUP, STD_LBL));
        masterWidgets.Add(MakeWidget("w_vergi",       "KDV Orani",          "percent", STD_GROUP, STD_LBL));
        masterWidgets.Add(MakeWidget("w_olusturma",   "Olusturma Tarihi",   "date",    STD_GROUP, STD_LBL));

        // Olcu Birimi � Standart Alanlar grubunda
        var unitOptions = allUnits.Select(u => (object)new Dictionary<string, object?>
        {
            ["value"] = u.Code,
            ["label"] = string.IsNullOrWhiteSpace(u.Name) ? u.Code : $"{u.Code} � {u.Name}",
        }).ToList();
        masterWidgets.Add(MakeWidget("w_unit", "�l�� Birimi", "options", STD_GROUP, STD_LBL, unitOptions));

        // 5 Grup slot'u � "Gruplamalar" grubunda collapsible
        for (int cat = 1; cat <= 5; cat++)
        {
            var groupsForCat = groupsByCat.TryGetValue(cat, out var l) ? l : new List<CalibraHub.Application.Contracts.MaterialGroupDto>();
            var options = groupsForCat.Select(g => (object)new Dictionary<string, object?>
            {
                ["value"] = g.GroupCode,
                ["label"] = string.IsNullOrWhiteSpace(g.GroupDescription) ? g.GroupCode : $"{g.GroupCode} � {g.GroupDescription}",
            }).ToList();
            masterWidgets.Add(MakeWidget($"w_grup{cat}", $"Grup {cat}", "options", GRP_GROUP, GRP_LBL, options));
        }

        // Aktif Ozellik widget'lari � "�zellikler ve Kombinasyonlar" grubunda
        foreach (var feat in activeFeatures)
        {
            var values = valuesByFeature.TryGetValue(feat.Id, out var vl) ? vl : new List<CalibraHub.Application.Contracts.ProductConfigurationValueDto>();
            var featOptions = values.Select(v => (object)new Dictionary<string, object?>
            {
                ["value"] = v.Id.ToString(),
                ["label"] = !string.IsNullOrWhiteSpace(v.Description) ? v.Description : (!string.IsNullOrWhiteSpace(v.Value) ? v.Value : v.Code),
            }).ToList();
            masterWidgets.Add(MakeWidget($"w_feat_{feat.Id}", feat.Name, "options", FEAT_GROUP, FEAT_LBL, featOptions));
        }

        var entities = await BuildMaterialCardEntitiesAsync(cards, handledColumns, activeFeatures, ct);

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
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MaterialCardEdit)]
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
            var itemsSchema = await _widgetService.GetFormSchemaByCodeAsync("ITEMS", ct);
            var handledColumns = ExtractHandledEntityColumns(itemsSchema);
            var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(ct);
            var activeFeatures = snapshot.Features.Where(f => f.IsActive).OrderBy(f => f.Name).ToList();
            var entities = await BuildMaterialCardEntitiesAsync(cards, handledColumns, activeFeatures, ct);
            return Json(new { entities, totalCount, page, pageSize });
        }
        catch (Exception ex)
        {
            return Json(new { error = "Islem sirasinda bir hata olustu." });
        }
    }

    private async Task<List<object>> BuildItemsMasterWidgetsAsync(CancellationToken ct)
    {
        var itemsSchema = await _widgetService.GetFormSchemaByCodeAsync("ITEMS", ct);
        return BuildItemsMasterWidgetsFromSchema(itemsSchema);
    }

    private static List<object> BuildItemsMasterWidgetsFromSchema(
        CalibraHub.Application.Contracts.WidgetFormSchemaDto? itemsSchema)
    {
        var masterWidgets = new List<object>();
        if (itemsSchema == null) return masterWidgets;
        foreach (var w in itemsSchema.Widgets.Where(w => w.IsActive
            && !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(w.DataType, "grid",  StringComparison.OrdinalIgnoreCase)))
        {
            var dt = w.DataType.ToLowerInvariant();
            // 2026-05-24: dropdown / multi-select widget'lari icin Options'i {value,label} formuna donustur.
            object? optionsArray = null;
            if ((dt == "dropdown" || dt == "multi-select" || dt == "multi_select" || dt == "multiselect")
                && w.Options != null && w.Options.Count > 0)
            {
                optionsArray = w.Options.Select(s => (object)new Dictionary<string, object?> {
                    ["value"] = s,
                    ["label"] = s,
                }).ToList();
            }
            var widget = new Dictionary<string, object?>
            {
                ["id"]           = w.WidgetCode,
                ["dbId"]         = w.Id,
                ["isPlainField"] = w.IsPlainField,
                ["type"]         = "data",
                ["dataType"]     = dt,
                ["label"]        = w.Label,
                // Admin Form Tasarimi'ndan tanimlanmis widget � filtre panelinde
                // "Widget Alanlari" grubunda gosterilsin (default 'standard' degil).
                ["source"]       = "widget",
            };
            if (optionsArray != null) widget["options"] = optionsArray;
            masterWidgets.Add(widget);
        }
        return masterWidgets;
    }

    /// <summary>
    /// 2026-05-24: Admin'in ITEMS formuna tanimladigi widget'lar arasinda IsSystemField=true
    /// olanlarin EntityColumn'larini (Pascal-case) tespit eder. Bu kolonlar zaten admin
    /// widget'iyla cover edildigi icin biz ayrica w_kod/w_ad/w_grup sistem widget'i
    /// EKLEMEYIZ � yoksa filtre panelinde duplikat alan gozukur ("Stok Adi" + ayni isim).
    /// </summary>
    private static HashSet<string> ExtractHandledEntityColumns(
        CalibraHub.Application.Contracts.WidgetFormSchemaDto? itemsSchema)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (itemsSchema == null) return set;
        foreach (var w in itemsSchema.Widgets)
        {
            if (!w.IsActive) continue;
            if (w.IsSystemField && !string.IsNullOrWhiteSpace(w.EntityColumn))
                set.Add(w.EntityColumn);
        }
        return set;
    }

    private async Task<List<object>> BuildMaterialCardEntitiesAsync(
        IReadOnlyCollection<ItemDto> cards,
        HashSet<string> handledPlainColumns,
        IReadOnlyList<CalibraHub.Application.Contracts.ProductConfigurationFeatureDto> activeFeatures,
        CancellationToken ct)
    {
        var recordIds = cards.Select(c => c.Id.ToString()).ToArray();
        var batchWidgets = recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("ITEMS", recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<CalibraHub.Application.Contracts.WidgetRenderDto>>();

        // 2026-05-24: Batch query'ler (N+1 onlemek icin).
        var itemIds = cards.Select(c => c.Id).ToArray();
        var groupMappings = itemIds.Length > 0
            ? await _logisticsConfigurationService.GetMaterialGroupMappingsBatchAsync(itemIds, ct)
            : new Dictionary<int, IReadOnlyList<CalibraHub.Application.Contracts.MaterialGroupMappingDto>>();
        var unitMappings = itemIds.Length > 0
            ? await _logisticsConfigurationService.GetItemUnitsBatchAsync(itemIds, ct)
            : new Dictionary<int, IReadOnlyList<CalibraHub.Domain.Entities.ItemUnit>>();
        var featMappings = itemIds.Length > 0
            ? await _logisticsConfigurationService.GetItemFeatureMappingsBatchAsync(itemIds, ct)
            : new Dictionary<int, IReadOnlyList<CalibraHub.Domain.Entities.ItemFeatureMapping>>();
        // UnitId ? UnitCode cevirici � ItemUnit.UnitId int, filter UnitCode string match yapar.
        var allUnitsLookup = (await _logisticsConfigurationService.GetUnitsAsync(ct))
            .ToDictionary(u => u.Id, u => u.Code, EqualityComparer<int>.Default);

        var trCulture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

        var entities = new List<object>();
        foreach (var card in cards)
        {
            var cardWidgets = new List<object>();
            var recordId = card.Id.ToString();

            // 2026-05-24: Plain field sistem widget'lari (Kod / Ad / Grup) � FilterPanel
            // entities[0].widgets'tan auto-discover edip "Standart Alanlar" grubunda gosterir.
            // Bu sayede kullanici Stok Adi / Kodu / Grubu uzerinden direkt filtreleyebilir.
            // ANCAK: admin ITEMS formuna IsSystemField+EntityColumn ile widget tanimlamissa
            // o kolonu cover ediyor ? cift gozukmemesi icin atla. (handledPlainColumns set'i.)
            if (!handledPlainColumns.Contains("Code"))
            {
                cardWidgets.Add(new {
                    id = "w_kod", type = "data", dataType = "text", label = "Stok Kodu",
                    value = (string?)card.Code,
                    detail = (string?)null,
                    color = "slate",
                    alwaysVisible = false,
                });
            }
            if (!handledPlainColumns.Contains("Name"))
            {
                cardWidgets.Add(new {
                    id = "w_ad", type = "data", dataType = "text", label = "Stok Adi",
                    value = (string?)card.Name,
                    detail = (string?)null,
                    color = "slate",
                    alwaysVisible = false,
                });
            }
            // 2026-05-24: 5 ayri Grup slot'u � her biri kendi MaterialGroup kategorisinden
            // multi-select filtre alani. Filter panel "options" dataType'inda chip-toggle UI uretir.
            // Card'da gosterim icin de description varsa onu, yoksa kodu yaziyoruz (detail field).
            IReadOnlyList<CalibraHub.Application.Contracts.MaterialGroupMappingDto>? cardMappings = null;
            groupMappings.TryGetValue(card.Id, out cardMappings);
            for (int cat = 1; cat <= 5; cat++)
            {
                var slot = cardMappings?.FirstOrDefault(m => m.SlotOrder == cat);
                cardWidgets.Add(new {
                    id = $"w_grup{cat}",
                    type = "data",
                    dataType = "options",
                    label = $"Grup {cat}",
                    value = (string?)slot?.GroupCode,
                    detail = (string?)(slot is null
                        ? null
                        : (string.IsNullOrWhiteSpace(slot.GroupDescription) ? slot.GroupCode : slot.GroupDescription)),
                    color = "violet",
                    alwaysVisible = false,
                });
            }

            // 2026-05-24: Olcu Birimi (multi-value) � default UnitId + ItemUnits hepsi.
            // value = "KG,ADT,M" comma-separated, frontend parseOptionsValue ile parser.
            var unitCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (card.UnitId.HasValue && allUnitsLookup.TryGetValue(card.UnitId.Value, out var defUC) && !string.IsNullOrEmpty(defUC))
                unitCodes.Add(defUC);
            if (unitMappings.TryGetValue(card.Id, out var itemUnitList))
            {
                foreach (var iu in itemUnitList)
                {
                    if (allUnitsLookup.TryGetValue(iu.UnitId, out var uc) && !string.IsNullOrEmpty(uc))
                        unitCodes.Add(uc);
                }
            }
            cardWidgets.Add(new {
                id = "w_unit",
                type = "data",
                dataType = "options",
                label = "�l�� Birimi",
                value = (string?)(unitCodes.Count == 0 ? null : string.Join(",", unitCodes)),
                detail = (string?)null,
                color = "blue",
                alwaysVisible = false,
            });

            // 2026-05-24: Her aktif Ozellik icin widget � bu kart icin secili FeatureValueId
            // listesi. Multi-value (bir item ayni feature'a birden cok degerle bagli olabilir).
            IReadOnlyList<CalibraHub.Domain.Entities.ItemFeatureMapping>? itemFeatList = null;
            featMappings.TryGetValue(card.Id, out itemFeatList);
            foreach (var feat in activeFeatures)
            {
                var valueIds = itemFeatList == null
                    ? new List<string>()
                    : itemFeatList
                        .Where(m => m.FeatureId == feat.Id && m.FeatureValueId.HasValue && m.IsActive)
                        .Select(m => m.FeatureValueId!.Value.ToString())
                        .Distinct()
                        .ToList();
                cardWidgets.Add(new {
                    id = $"w_feat_{feat.Id}",
                    type = "data",
                    dataType = "options",
                    label = feat.Name,
                    value = (string?)(valueIds.Count == 0 ? null : string.Join(",", valueIds)),
                    detail = (string?)null,
                    color = "violet",
                    alwaysVisible = false,
                });
            }

            // 2026-05-23: Sistem widget'lari � Ihtiya� Kaydi pattern'i ile ozdes.
            // FilterPanel entity.widgets'tan auto-discover ettigi icin "standart" alanlar
            // olarak filtrelenebilir hale gelir. Kart ekraninda chip olarak da gorunurler.
            cardWidgets.Add(new {
                id = "w_aktif", type = "data", dataType = "boolean", label = "Durum",
                value = card.IsActive,
                detail = (string?)(card.IsActive ? "Aktif" : "Pasif"),
                color = card.IsActive ? "emerald" : "slate",
                alwaysVisible = true,
            });
            cardWidgets.Add(new {
                id = "w_kombinasyon", type = "data", dataType = "boolean", label = "Kombinasyon Takibi",
                value = card.Combinations,
                detail = (string?)(card.Combinations ? "Var" : "Yok"),
                color = card.Combinations ? "violet" : "slate",
                alwaysVisible = true,
            });
            cardWidgets.Add(new {
                id = "w_vergi", type = "data", dataType = "percent", label = "KDV Orani",
                value = card.TaxRate.ToString("0.##", trCulture),
                detail = "%",
                color = "indigo",
                alwaysVisible = true,
            });
            if (card.Created.HasValue)
            {
                cardWidgets.Add(new {
                    id = "w_olusturma", type = "data", dataType = "date", label = "Olusturma Tarihi",
                    value = card.Created.Value.ToString("yyyy-MM-dd"),
                    detail = (string?)null,
                    color = "slate",
                    alwaysVisible = true,
                });
            }

            if (batchWidgets.TryGetValue(recordId, out var renderDtos))
            {
                foreach (var w in renderDtos)
                {
                    cardWidgets.Add(new {
                        id             = w.WidgetId,
                        widgetId       = w.WidgetId,
                        type           = "data",
                        dataType       = w.DataType.ToLowerInvariant(),
                        label          = w.Label,
                        value          = w.Value,
                        // Metadata � guide-list / lookup widget'lari icin guideCode + guideConfig.
                        // SmartWidget guide-list popup'inda metadata.guideCode ile rehber acar.
                        metadata       = w.Metadata,
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

            // Record values � Items tablosu kolon adlariyla (snake_case). SmartCard
            // widget'larina (ozellikle guide-list popup'una) dogrudan erisim icin.
            // Token resolve liste sayfasinda DOM olmadigi icin bu dictionary'yi kullanir.
            var recordValues = new Dictionary<string, object?>
            {
                ["id"]          = card.Id,
                ["code"]        = card.Code,
                ["name"]        = card.Name,
                ["type_id"]     = card.TypeId,
                ["unit"]        = card.UnitId,
                ["tax_rate"]    = card.TaxRate,
                ["combinations"]= card.Combinations,
                ["is_active"]   = card.IsActive,
                ["create_date"] = card.Created,
                ["modify_date"] = card.Updated,
            };

            entities.Add(new {
                id = card.Id,
                title = card.Name ?? "(adsiz)",
                subtitle = card.Code ?? string.Empty,
                description = string.Empty,
                imageUrl = (string?)null,
                statusBadge = (object?)null,
                recordValues = recordValues,
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
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MaterialCardEdit)]
    public async Task<IActionResult> MaterialCardEdit(int? id, CancellationToken cancellationToken)
    {
        ViewData["MaterialCardEditId"] = id ?? 0;

        // Yeni EAV widget renderer icin integer Id - ViewBag'e aktar.
        ViewBag.ItemId = id.HasValue && id.Value > 0 ? id.Value.ToString() : string.Empty;

        return View();
    }

    /// <summary>
    /// Malzeme kartı "Stok Hareketleri" sekmesi verisi. DocumentLine (MovementType dolu)
    /// tabanlı hareket ekstresi + koşan bakiye + özet + filtre lokasyonları. Filtreler
    /// opsiyonel; koşan bakiye tüm geçmiş üzerinden hesaplanır, filtreler sadece gösterimi
    /// daraltır. Excel export gösterilen satırlardan client tarafında üretilir.
    /// </summary>
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MaterialCardEdit)]
    public async Task<IActionResult> ItemStockMovements(
        int itemId,
        DateTime? fromDate,
        DateTime? toDate,
        byte? movementType,
        int? locationId,
        [FromServices] Application.Abstractions.Persistence.IStockMovementQueryRepository stockMovements,
        CancellationToken cancellationToken)
    {
        if (itemId <= 0)
            return Json(new { ok = false, error = "Malzeme kartı ID gerekli." });

        try
        {
            var filter = new ItemStockMovementFilter(
                itemId,
                fromDate,
                toDate,
                movementType is >= 1 and <= 4 ? movementType : null,
                locationId is > 0 ? locationId : null);

            var result = await stockMovements.ListForItemAsync(filter, cancellationToken);

            return Json(new
            {
                ok = true,
                rows = result.Rows.Select(m => new
                {
                    m.LineId,
                    m.DocumentId,
                    m.DocumentNumber,
                    movementDate = m.MovementDate.ToString("yyyy-MM-dd"),
                    m.DocTypeCode,
                    m.DocTypeName,
                    m.MovementType,
                    m.MovementLabel,
                    m.Quantity,
                    m.SignedQuantity,
                    m.RunningBalance,
                    m.UnitCode,
                    m.FromLocationId,
                    m.FromLocationCode,
                    m.FromLocationName,
                    m.ToLocationId,
                    m.ToLocationCode,
                    m.ToLocationName,
                    m.UnitCost,
                    m.LotNo,
                    m.CombinationCode,
                    m.Notes,
                    m.CreatedByName,
                }),
                locations = result.Locations.Select(l => new { l.Id, l.Label }),
                summary = new
                {
                    result.TotalIn,
                    result.TotalOut,
                    result.CurrentBalance,
                    result.MovementCount,
                },
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ItemStockMovements] itemId={ItemId} hareket sorgusu hatası", itemId);
            return Json(new { ok = false, error = "Stok hareketleri yüklenemedi." });
        }
    }

    // NOT: BOM (Urun Agaci/Recete) endpoint'leri BomController'a tasindi (rapor �2.3).
    // Tasinmis: BOMs, BOMEdit, GetBOMsPage, GetBOM, GetBOMById, DeleteBOMJson, SaveBOM.
    // GetMaterialCost burada kaldi (PriceListService + CurrencyService + CardGroupRepo bagimliligi).

    /// <summary>
    /// Standart Maliyet Goruntuleme endpoint'i � bir malzemenin recetesindeki bilesenleri
    /// secilen fiyat grubundan fiyatlandirir, satir ve toplam maliyetleri doner.
    ///
    /// Request: materialCode (zorunlu), configCode (ops.), priceGroupId (zorunlu),
    ///          currencyId (zorunlu), priceType (varsayilan "Buy"), quantity (varsayilan 1)
    ///
    /// Response: { found, parent, components: [{ code, name, qty, scrapRatio, unitPrice, lineCost }],
    ///             totalCost, currency }
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMaterialCost(
        string materialCode,
        string? configCode,
        int priceGroupId,
        int currencyId,
        string? priceType,
        decimal quantity = 1m,
        // validOn: ISO 'yyyy-MM-dd'. Bos ise bugunun tarihi (UtcNow). Geriye doniik
        // tekliflerde frontend BELGE TARIHINI gonderir ? gecmis fiyatlar dogru lookup.
        string? validOn = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            return BadRequest(new { found = false, message = "Malzeme kodu zorunlu." });
        if (priceGroupId <= 0 || currencyId <= 0)
            return BadRequest(new { found = false, message = "Fiyat grubu ve para birimi zorunlu." });

        DateTime priceDate = DateTime.UtcNow.Date;
        if (!string.IsNullOrWhiteSpace(validOn) && DateTime.TryParse(validOn,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal, out var parsedDate))
        {
            priceDate = parsedDate.Date;
        }

        var bom = await _logisticsConfigurationService.GetBOMByCodeAsync(materialCode, configCode, cancellationToken);
        if (bom is null || bom.Lines is null || bom.Lines.Count == 0)
            return Ok(new { found = false, message = "Bu malzeme icin recete tanimli degil." });

        // BOMLineWithName artik dogrudan ItemId/ConfigId tasiyor (FK-based BOM refactor sonrasi)
        // � ekstra Items.code lookup gerekmiyor.
        var keys = new List<PriceEntryKey>();
        var compMeta = new List<(string Code, string Name, string? CfgCode, decimal Qty, decimal Scrap, int ItemId, int? ConfigId)>();
        foreach (var l in bom.Lines)
        {
            keys.Add(new PriceEntryKey(l.ItemId, l.ConfigId));
            compMeta.Add((l.ComponentMaterialCode, l.ComponentMaterialName, l.ComponentConfigCode, l.Quantity, l.ScrapRatio, l.ItemId, l.ConfigId));
        }

        // DEBUG: maliyet teshisi (gecici)
        _logger.LogDebug("[CostView] material={MaterialCode} configCode={ConfigCode} priceGroup={PriceGroupId} currency={CurrencyId} priceType={PriceType} validOn={ValidOn:yyyy-MM-dd}", materialCode, configCode, priceGroupId, currencyId, priceType, priceDate);
        foreach (var l in bom.Lines)
            _logger.LogDebug("[CostView]   BOM bilesen: code={ComponentCode} itemId={ItemId} configId={ConfigId} qty={Qty}", l.ComponentMaterialCode, l.ItemId, l.ConfigId, l.Quantity);

        // PriceType DB konvansiyonu: 'b'/'s'/'m'. Bos ise varsayilan 'm' (Maliyet).
        var pType = string.IsNullOrWhiteSpace(priceType) ? "m" : priceType.Trim();
        var prices = keys.Count == 0
            ? Array.Empty<ExistingPriceRow>()
            : (await _priceListService.GetExistingPricesAsync(
                new GetExistingPricesRequest(priceGroupId, currencyId, pType, priceDate, keys),
                cancellationToken)).ToArray();

        var priceByKey = prices.ToDictionary(p => (p.ItemId, p.ConfigId), p => p.Price);
        var currencies = await _currencyService.GetAllAsync(cancellationToken);
        var currency = currencies.FirstOrDefault(c => c.Id == currencyId);

        // Stok grup kodlari (card_group_mappings � entityType=1=Item) � paralel batch.
        // Her bilesen icin level=1 ve level=2 grup kodlari eklenir; UI'da
        // gruplama opsiyonu ile bu kodlara gore kirilim yapar.
        var groupTasks = compMeta
            .Select(c => c.ItemId)
            .Distinct()
            .Select(async id =>
            {
                try
                {
                    var ms = await _cardGroupRepo.GetEntityMappingsAsync(1, id.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
                    return (ItemId: id, Map: ms.ToDictionary(m => (int)m.Level, m => (Code: m.Code, Desc: m.Description)));
                }
                catch
                {
                    return (ItemId: id, Map: new Dictionary<int, (string Code, string? Desc)>());
                }
            })
            .ToArray();
        var groupResults = await Task.WhenAll(groupTasks);
        var groupsByItem = groupResults.ToDictionary(t => t.ItemId, t => t.Map);

        decimal total = 0m;
        var components = compMeta.Select(c =>
        {
            decimal price = priceByKey.TryGetValue((c.ItemId, c.ConfigId), out var p) ? p : 0m;
            // Fire ratio dahil edilmis efektif miktar � recete bileseninin gercek tuketim adedi
            decimal effQty = c.Qty * quantity * (1m + c.Scrap);
            decimal lineCost = effQty * price;
            total += lineCost;
            // Grup kodlari � tum seviyeler (level 1, 2, 3, ...) dinamik olarak doner.
            // Frontend her bilesen icin gormekte oldugu seviyeleri toplar ve checkbox
            // listesini ona gore uretir; sabit g1/g2 sayisi kalmadi.
            var groupsObj = new Dictionary<string, object>();
            if (groupsByItem.TryGetValue(c.ItemId, out var gmap))
            {
                foreach (var kv in gmap)
                    groupsObj[kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new { code = kv.Value.Code, name = kv.Value.Desc };
            }
            return new
            {
                code = c.Code,
                name = c.Name,
                configCode = c.CfgCode,
                qty = c.Qty,
                scrapRatio = c.Scrap,
                effectiveQty = effQty,
                unitPrice = price,
                lineCost = lineCost,
                hasPrice = price > 0m,
                groups = groupsObj,   // { "1": {code,name}, "2": {code,name}, "3": {code,name}, ... }
            };
        }).ToArray();

        return Ok(new
        {
            found = true,
            parent = new
            {
                itemId       = bom.ItemId,
                materialCode = bom.ItemCode,
                materialName = bom.ItemName,
                configId     = bom.ConfigId,
                configCode   = bom.ConfigCode,
                description  = bom.Description,
            },
            quantity = quantity,
            components,
            totalCost = total,
            currency = new
            {
                id = currency?.Id ?? currencyId,
                code = currency?.Code,
                name = currency?.Name,
                symbol = currency?.Symbol,
            },
            priceType = pType,
        });
    }

    // NOT: SaveBOM endpoint'i BomController'a tasindi (rapor �2.3).

    [HttpGet]
    public async Task<IActionResult> StockLookup(string? q, CancellationToken cancellationToken)
    {
        var cards = await _logisticsConfigurationService.GetItemsForLookupAsync(cancellationToken);
        var query = (q ?? "").Trim().ToLowerInvariant();
        var filtered = cards
            .Where(s => string.IsNullOrEmpty(query)
                     || s.Code.ToLowerInvariant().Contains(query)
                     || (s.Name?.ToLowerInvariant().Contains(query) ?? false));

        if (!string.IsNullOrEmpty(query))
            filtered = filtered.Take(50);

        // Unit lookup � UnitId varsa UnitCode'u resolve et (single fetch + dictionary)
        var units = await _logisticsConfigurationService.GetUnitsAsync(cancellationToken);
        var unitMap = units.ToDictionary(u => u.Id, u => u.Code);

        var results = filtered
            .Select(s => new
            {
                id        = s.Id,                    // Items.Id � frontend itemId hidden alana yazar
                code      = s.Code.Trim(),
                name      = s.Name,
                hasConfig = s.Combinations,
                unitId    = s.UnitId,                // mamulun ana birim FK (Items.UnitId)
                unitCode  = s.UnitId.HasValue && unitMap.TryGetValue(s.UnitId.Value, out var uc) ? uc : null
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
            configId = c.ConfigId,
            code   = c.Code,
            name   = c.Name,
            features = c.FeatureValues.Select(fv => new { feature = fv.Feature, value = fv.Value, valueCode = fv.ValueCode }).ToArray()
        }));
    }


    [HttpGet]
    public IActionResult Locations() => View();

    // -- Olcu Birimi + Lokasyon (rapor �2.3 split) ---------------------
    // Units, UnitsBoardEntities, UnitToggle, UnitEdit + JSON endpoint'leri
    // UnitController'a tasindi.
    // GetAllLocations, GetLocation, SaveLocationJson, DeleteLocationJson
    // LocationController'a tasindi.
    // SaveUnit/DeleteUnit/SaveLocation/DeleteLocation form-post endpoint'leri
    // kaldirildi (modern stack JSON kullanir, kullanilmiyordu).

    /* �"��"� Lokasyon JSON Endpoint'leri �"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"� */

    // NOT: Lokasyon JSON endpoint'leri (GetAllLocations, GetLocation, SaveLocationJson,
    // DeleteLocationJson) LocationController'a tasindi.

    /// <summary>
    /// LocationTree React component'i icin tree config � nested location agaci
    /// + types lookup + widget'lar (CalibraSmartBoard standardi).
    /// Hiyerarsinin tum seviyeleri ayni widget setini kullanir (formCode = LOCATIONS).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> LocationsTree(CancellationToken ct)
    {
        const string formCode = "LOCATIONS";
        var all   = await _logisticsConfigurationService.GetLocationsAsync(ct);
        var types = await _logisticsConfigurationService.GetLocationTypesAsync(ct);
        var typeMap = types.ToDictionary(t => t.Code, t => t.Name, StringComparer.OrdinalIgnoreCase);

        // -- Master widget sablonu (admin SmartBoardConfigPanel + filter panel i�in) --
        var schema = await _widgetService.GetFormSchemaByCodeAsync(formCode, ct);
        var masterWidgets = CalibraHub.Web.Helpers.SmartBoardFilterHelpers.BuildAdminFormWidgets(schema);
        var typeOptions  = CalibraHub.Web.Helpers.SmartBoardFilterHelpers.ToOptionsList(types.Select(t => t.Name));
        var usageOptions = CalibraHub.Web.Helpers.SmartBoardFilterHelpers.ToOptionsList(new[] { "Makine", "Depo", "Makine + Depo" });
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeOptionsWidget("w_type",     "Tip",       typeOptions));
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget   ("w_status",   "Durum",     "boolean"));
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeOptionsWidget("w_usage",    "Kullanim",  usageOptions));
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget   ("w_children", "Alt Sayi",  "numeric"));
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget   ("w_depth",    "Seviye",    "numeric"));

        // -- Batch widget degerleri � t�m lokasyonlar i�in tek seferde --
        var recordIds = all.Select(l => l.Id.ToString()).ToArray();
        var batchWidgets = (schema != null && recordIds.Length > 0)
            ? await _widgetService.GetBatchRenderModelsAsync(formCode, recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        // -- Child sayilarini bir kerede hesapla --
        var childCount = all.GroupBy(l => l.ParentId ?? 0)
                           .ToDictionary(g => g.Key, g => g.Count());
        // -- Derinlik (seviye) hesapla --
        int ComputeDepth(int locId)
        {
            var depth = 1; var guard = 0;
            var byIdMap = all.ToDictionary(x => x.Id);
            var cur = byIdMap.TryGetValue(locId, out var n) ? n : null;
            while (cur?.ParentId is int pid && byIdMap.TryGetValue(pid, out var par) && guard++ < 50)
            { depth++; cur = par; }
            return depth;
        }

        // Nested tree olustur � Dictionary<string,object?> kullaniyoruz cunku
        // anonim type'lar immutable; widget'lari sonradan eklemek icin mutable lazim.
        var nodes = new List<Dictionary<string, object?>>();
        foreach (var l in all.OrderBy(x => x.SortOrder).ThenBy(x => x.LocationCode))
        {
            var typeSort = !string.IsNullOrEmpty(l.LocationTypeCode)
                ? (types.FirstOrDefault(t => string.Equals(t.Code, l.LocationTypeCode, StringComparison.OrdinalIgnoreCase))?.SortOrder ?? 0)
                : 0;
            var typeName = !string.IsNullOrEmpty(l.LocationTypeCode) && typeMap.TryGetValue(l.LocationTypeCode, out var tn)
                ? tn : GetLocationTypeDisplayName(l.LocationTypeCode);

            // -- Widget degerleri --
            var widgets = new List<object>();
            // Sistem widget'lari (her zaman dolu)
            widgets.Add(new { id = "w_type",   type = "data", dataType = "options", label = "Tip", value = typeName, color = "indigo" });
            widgets.Add(new { id = "w_status", type = "data", dataType = "text", label = "Durum",
                value = l.IsActive ? "Aktif" : "Pasif", color = l.IsActive ? "emerald" : "slate" });

            string? usageLabel = null; string usageColor = "slate";
            if (l.IsMachinePark && l.IsStorageArea) { usageLabel = "Makine + Depo"; usageColor = "violet"; }
            else if (l.IsMachinePark)                { usageLabel = "Makine"; usageColor = "indigo"; }
            else if (l.IsStorageArea)                { usageLabel = "Depo";   usageColor = "emerald"; }
            if (usageLabel != null)
                widgets.Add(new { id = "w_usage", type = "data", dataType = "text", label = "Kullanim", value = usageLabel, color = usageColor });

            var nChild = childCount.TryGetValue(l.Id, out var c) ? c : 0;
            if (nChild > 0)
                widgets.Add(new { id = "w_children", type = "data", dataType = "numeric", label = "Alt", value = nChild.ToString(System.Globalization.CultureInfo.InvariantCulture), detail = "adet", color = "slate" });
            widgets.Add(new { id = "w_depth", type = "data", dataType = "numeric", label = "Seviye", value = ComputeDepth(l.Id).ToString(System.Globalization.CultureInfo.InvariantCulture), color = "slate" });

            // Dinamik widget'lar (WidgetTra)
            var recordId = l.Id.ToString();
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

            nodes.Add(new Dictionary<string, object?>
            {
                ["id"]            = l.Id,
                ["parentId"]      = l.ParentId,
                ["code"]          = l.LocationCode,
                ["name"]          = l.LocationName ?? string.Empty,
                ["typeCode"]      = NormalizeLocationTypeCode(l.LocationTypeCode),
                ["typeName"]      = typeName,
                ["typeSortOrder"] = typeSort,
                ["sortOrder"]     = l.SortOrder,
                ["isActive"]      = l.IsActive,
                ["isMachinePark"] = l.IsMachinePark,
                ["isStorageArea"] = l.IsStorageArea,
                ["widgets"]       = widgets,
                ["children"]      = new List<Dictionary<string, object?>>(),
            });
        }

        var byId  = nodes.ToDictionary(n => (int)n["id"]!);
        var roots = new List<Dictionary<string, object?>>();
        foreach (var n in nodes)
        {
            var pid = (int?)n["parentId"];
            if (pid.HasValue && byId.TryGetValue(pid.Value, out var parent))
                ((List<Dictionary<string, object?>>)parent["children"]!).Add(n);
            else
                roots.Add(n);
        }

        return Json(new
        {
            boardKey   = "logistics-locations-tree",
            formCode,
            title      = "Lokasyon Tanimlamalari",
            icon       = "MapPin",
            iconColor  = "indigo",
            roots,
            types = types.Select(t => new { code = t.Code, name = t.Name, sortOrder = t.SortOrder })
                         .OrderBy(t => t.sortOrder).ThenBy(t => t.code).ToArray(),
            masterWidgets,
            saveUrl         = "/Logistics/SaveLocationJson",
            deleteUrl       = "/Logistics/DeleteLocationJson",
            usageCheckUrl   = "/Logistics/GetLocationUsageJson",
            refreshUrl      = "/Logistics/LocationsTree",
            maxDepth        = 7,
        });
    }

    // -- Makine Tanimlamalari (rapor �2.3 � pilot split) -----------------
    // Makine aggregate'i icin tum endpoint'ler MachineController'a tasindi.
    // URL preservation: /Logistics/Machines, /Logistics/MachineEdit gibi rotalar
    // MachineController'da [Route("Logistics/[action]")] ile aynen calisir.
    // Tasinmis endpoint'ler:
    //   GET  /Logistics/Machines, /Logistics/MachineEdit, /Logistics/GetAllMachines
    //   POST /Logistics/SaveMachineJson, /Logistics/DeleteMachineJson

    /* �"��"� Malzeme Grupları �"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"� */

    // NOT: Malzeme Grubu endpoint'leri MaterialGroupController'a tasindi (rapor �2.3 split).
    // Tasinmis: MaterialGroups, MaterialGroupEdit, SaveMaterialGroupJson, DeleteMaterialGroupJson,
    // GetAllMaterialGroups, UpsertMaterialGroup, DeleteMaterialGroupInline, MaterialGroupLookup,
    // GetMaterialGroupMappings, SaveMaterialGroupMappings

    [HttpPost]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MaterialCardEdit)]
    public async Task<IActionResult> SaveMaterialCard(
        [Bind(Prefix = "StockInput")] MaterialCardCreateInput stockInput,
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
                    string.Equals(x.Code, stockInput.Code.Trim(), StringComparison.OrdinalIgnoreCase));

                if (existingMaterialCard is not null)
                {
                    stockInput.ItemId = existingMaterialCard.Id;
                }
            }

            var isUpdate = stockInput.ItemId.HasValue && stockInput.ItemId.Value != 0;
            var currentUserName = User.FindFirstValue(System.Security.Claims.ClaimTypes.Name);

            if (isUpdate)
            {
                await _logisticsConfigurationService.UpdateItemAsync(
                    new UpdateItemRequest(
                        ItemId: stockInput.ItemId!.Value,
                        Code: stockInput.Code,
                        Name: stockInput.Name,
                        TypeId: stockInput.TypeId,
                        UnitId: stockInput.UnitId,
                        Combinations: stockInput.Combinations),
                    cancellationToken);
            }
            else
            {
                await _logisticsConfigurationService.CreateItemAsync(
                    new CreateItemRequest(
                        Code: stockInput.Code,
                        Name: stockInput.Name,
                        TypeId: stockInput.TypeId,
                        UnitId: stockInput.UnitId,
                        Combinations: stockInput.Combinations),
                    cancellationToken);
            }

            // Kaydedilen/guncellenen kartin ID'sini bul
            var savedCardId = stockInput.ItemId;
            if (!isUpdate && (savedCardId == null || savedCardId == 0))
            {
                var refreshed = await _logisticsConfigurationService.GetSnapshotAsync(cancellationToken);
                var created = refreshed.Items
                    .FirstOrDefault(x => string.Equals(x.Code, stockInput.Code, StringComparison.OrdinalIgnoreCase));
                if (created != null) savedCardId = created.Id;
            }

            TempData["AdminSuccess"] = isUpdate
                ? "Malzeme karti guncellendi."
                : "Malzeme karti kaydedildi.";
            return RedirectToMaterialCards(new { id = savedCardId });
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
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.MaterialCardEdit)]
    public async Task<IActionResult> DeleteMaterialCard(
        int stockCardId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _logisticsConfigurationService.DeactivateItemAsync(stockCardId, cancellationToken);

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
        List<int>? propertyIds,
        CancellationToken cancellationToken)
    {
        try
        {
            await _logisticsConfigurationService.ConfigureItemAsync(
                new ConfigureItemRequest(
                    stockCardId,
                    isConfigurable,
                    (propertyIds ?? new List<int>()).ToArray()),
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
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ProductConfig)]
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

    // --------------------------------------------------------------------
    // "Tanimli Kombinasyonlar" liste ekrani � Lojistik > �zellik ve Kombinasyon alti.
    // T�m aktif kombinasyonlarin kart listesi (kombinasyon kodu + parent stok +
    // �zellik/deger chip'leri). Karti tiklayinca parent stogun kombinasyon tab'ina
    // navigate eder (matchPath ile MaterialCard tab'i reuse).
    // --------------------------------------------------------------------
    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.ProductCombinations)]
    public async Task<IActionResult> Combinations(CancellationToken cancellationToken)
    {
        var board = await BuildCombinationsBoardConfigAsync(cancellationToken);
        return View(new CombinationsSmartBoardViewModel { BoardConfig = board });
    }

    [HttpGet("/Logistics/CombinationsBoardConfig")]
    public async Task<IActionResult> CombinationsBoardConfig(CancellationToken cancellationToken)
    {
        var board = await BuildCombinationsBoardConfigAsync(cancellationToken);
        return Json(board);
    }

    // NOT: DeleteCombinationJson CombinationController'a tasindi (rapor �2.3 split).

    private async Task<object> BuildCombinationsBoardConfigAsync(CancellationToken ct)
    {
        var combos = await _logisticsConfigurationService.GetAllCombinationsAsync(ct);
        var masterWidgets = new List<object>
        {
            CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_status",        "Durum",   "boolean"),
            CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_item",          "Mamul",   "text"),
            CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_feature_count", "�zellik", "numeric"),
        };

        var entities = new List<object>();
        foreach (var c in combos)
        {
            var widgets = new List<object>
            {
                new {
                    id = "w_status", type = "data", dataType = "text",
                    label = "Durum",
                    value = c.IsActive ? "Aktif" : "Pasif",
                    detail = (string?)null,
                    color = c.IsActive ? "emerald" : "slate"
                },
                new {
                    id = "w_item", type = "data", dataType = "text",
                    label = "Mamul",
                    value = c.ItemCode ?? "(?)",
                    detail = c.ItemName,
                    color = "blue"
                },
                new {
                    id = "w_feature_count", type = "data", dataType = "numeric",
                    label = "�zellik",
                    value = c.FeatureValues.Count.ToString(),
                    detail = "adet",
                    color = "indigo"
                },
            };

            // Her �zellik/deger ciftini ayri widget olarak ekle
            int idx = 1;
            foreach (var fv in c.FeatureValues)
            {
                widgets.Add(new
                {
                    id = $"w_fv_{idx}",
                    type = "data",
                    dataType = "text",
                    label = fv.Feature,
                    value = fv.Value,
                    detail = (string?)null,
                    color = "violet"
                });
                idx++;
            }

            entities.Add(new
            {
                id            = c.ConfigId,
                title         = c.Code,
                subtitle      = c.ItemCode is null ? null : $"{c.ItemCode} � {c.ItemName}",
                description   = c.Name,
                imageUrl      = (string?)null,
                statusBadge   = (object?)null,
                widgets,
                primaryAction = new
                {
                    label      = "Stok Kartinda A�",
                    icon       = "ExternalLink",
                    color      = "amber",
                    // Kombinasyon parent stok kartinin kombinasyon tab'ina y�nlenir.
                    url        = c.ItemId.HasValue ? $"/Logistics/MaterialCardEdit?id={c.ItemId.Value}#combinations" : "#",
                    hideButton = true,
                    // YENI SEKMEDE a� � "Tanimli Kombinasyonlar" listesi a�ik kalir,
                    // mevcut Malzeme Kartlari tab'i varsa onu reuse eder (matchPath).
                    openInTab  = new { title = "Malzeme Kartlari", matchPath = "/Logistics/MaterialCard" },
                },
                secondaryAction = new
                {
                    label     = "Sil",
                    icon      = "Trash2",
                    apiUrl    = $"/Logistics/DeleteCombinationJson?id={c.ConfigId}",
                    apiMethod = "POST",
                    confirm   = $"Bu kombinasyonu silmek istediginize emin misiniz? ({c.Code})",
                },
            });
        }

        return new
        {
            boardKey          = "logistics-combinations",
            title             = "Tanimli Kombinasyonlar",
            subtitle          = $"{entities.Count} kombinasyon",
            icon              = "Grid3X3",
            iconColor         = "violet",
            refreshUrl        = "/Logistics/CombinationsBoardConfig",
            searchPlaceholder = "Kombinasyon kodu, mamul veya deger ara�",
            emptyText         = "Hen�z tanimli kombinasyon yok",
            actions           = Array.Empty<object>(),
            masterWidgets,
            entities,
        };
    }

    // ════════════════════════════════════════════════════════════════
    // BuildProductConfigurationBoardConfigAsync
    //
    // Urun Konfigurasyonu (Features/Ozellikler) icin SmartBoard kart config'i
    // uretir. Her feature bir kart - icinde DataType, deger sayisi, ornek
    // degerler, bagli stok sayisi, aktif/pasif durumu widget'lari. Admin
    // panelden sales_quotes/contact_accounts gibi dynamic widget tanimlamak
    // icin "product_configuration" screenCode'u ile schema cagrisi yapilir.
    // ════════════════════════════════════════════════════════════════
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

            // �"��"� Sistem widget'lari �"��"�
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

        // �"��"� Master widget sablonu (⚙ SmartBoardConfigPanel icin) �"��"�
        // Sistem widget'lari sabit; PRODUCT_CONFIG form kodundaki admin widget'lar ekleniyor.
        var pcSchema = await _widgetService.GetFormSchemaByCodeAsync("PRODUCT_CONFIG", ct);
        var masterWidgets = CalibraHub.Web.Helpers.SmartBoardFilterHelpers.BuildAdminFormWidgets(pcSchema);
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("sys_datatype",     "Veri Tipi",      "text"));
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("sys_unit",         "Olcu Birimi",    "text"));
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("sys_value_count",  "Deger Sayisi",   "numeric"));
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("sys_value_sample", "Ornek Degerler", "text"));
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("sys_stock_count",  "Bagli Stok",     "numeric"));

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

    /// <summary>ConfigurationFieldDataType (enum) → Turkce etiket</summary>
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

    /// <summary>DataType → SmartBoard color palette</summary>
    private static string DataTypeColor(string? raw) => raw?.ToLowerInvariant() switch
    {
        "text"    => "blue",
        "numeric" => "amber",
        "number"  => "amber",
        "date"    => "cyan",
        "boolean" => "violet",
        _ => "slate",
    };

    // NOT: ProductFeatureEdit, GetProductFeature, SaveProductFeatureJson, DeleteProductFeatureJson,
    // SaveProductValueJson, DeleteProductValueJson, UpdateProductValueJson, SaveProductFeatureStocksJson
    // ProductFeatureController'a tasindi (rapor �2.3 split). DTO record'lar oraya tasindi.

    // NOT: Legacy form-post cluster (SaveProductFeature, SaveProductValue, SaveProductConfig, UpdateProductFeature, DeleteProductFeature, SaveProductFeatureStocks, DeleteProductValue, DeleteProductConfig) kaldirildi - UI artik *Json variantlarini cagiriyor (rapor 2.3 split + temizlik).

    // NOT: StockCodesJson + CombinationsDataJson CombinationController'a tasindi (rapor sec 2.3 split).

    [HttpGet]
    public async Task<IActionResult> ProductCombinations(
        string? stockCode,
        CancellationToken cancellationToken)
    {
        var snapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(cancellationToken);
        var stockSnapshot = await _logisticsConfigurationService.GetSnapshotAsync(cancellationToken);

        var allStockCodes = stockSnapshot.Items
            .Where(x => x.Combinations)
            .Select(x => x.Code)
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
                var st = stockSnapshot.Items.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
                var textLabel = st != null ? $"{code} - {st.Name}" : code;
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
            ? stockSnapshot.Items.FirstOrDefault(x => string.Equals(x.Code, resolvedStockCode, StringComparison.OrdinalIgnoreCase))
            : null;

        return View(new ProductCombinationsViewModel
        {
            StockCodeOptions = stockCodeOptions,
            SelectedStockCode = resolvedStockCode,
            SelectedStockId = selectedItem?.Id,
            SelectedStockName = selectedItem?.Name,
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

        // Tüm eski konfigurasyonlari sil (yeniden uretecegiz)
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

    // NOT: SaveProductCombinationsJson + UpdateCombinationDescriptionJson + AddSingleCombinationJson CombinationController'a tasindi (rapor 2.3 split).

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

    // NOT: BuildProductConfigurationViewModelAsync kaldirildi (sadece form-post legacy cluster kullaniyordu).


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
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Where(x =>
                string.IsNullOrWhiteSpace(normalizedSearch) ||
                ContainsInsensitive(x.Code, normalizedSearch) ||
                ContainsInsensitive(x.Name, normalizedSearch))
            .Select(x => new UnitRowViewModel
            {
                Id = x.Id,
                UnitCode = x.Code,
                UnitName = x.Name,
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
                Code = x.Code,
                Name = x.Name,
                TypeId = x.TypeId,
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
        if (!string.IsNullOrWhiteSpace(stockInput.Code))
        {
            var productSnapshot = await _logisticsConfigurationService.GetProductConfigurationSnapshotAsync(cancellationToken);

            var stockCombinations = productSnapshot.Configurations
                .Where(x => string.Equals(x.RelatedMaterialCode, stockInput.Code, StringComparison.OrdinalIgnoreCase)
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
                    Code = x.Code,
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
                        CreatedByUserId = null,
                        CreatedDate = x.Created,
                        ModifiedByUserId = null,
                        ModifiedDate = x.Updated
                    })
                    .FirstOrDefault()
                : null,
            Combinations = combinations,
            MeasureUnits = (await _logisticsConfigurationService.GetUnitsAsync(cancellationToken))
                .Where(u => u.IsActive)
                .Select(u => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(u.Name, u.Code))
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
            (MaterialCardListSortOptions.Name, "desc") => query
                .OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase),
            (MaterialCardListSortOptions.Name, _) => query
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase),
            (_, "desc") => query
                .OrderByDescending(x => x.Code, StringComparer.OrdinalIgnoreCase),
            _ => query
                .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
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
            new[] { row.Code, row.Name }
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
            Code = stockCard.Code,
            Name = stockCard.Name,
            TypeId = stockCard.TypeId,
            UnitId = stockCard.UnitId,
            Combinations = stockCard.Combinations
        };
    }


    private static Guid IntToGuid(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    // NOT: GetDataTypeLabel + NormalizeProductConfigurationTab kaldirildi (legacy form-post cluster ile birlikte).

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
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var text = string.Equals(x.Code, x.Name, StringComparison.OrdinalIgnoreCase)
                    ? x.Code
                    : $"{x.Code} - {x.Name}";
                return new SelectListItem(text, x.Code);
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

        if (userId.HasValue && userId.Value > 0 && resolvedPageSize != storedPageSize)
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

    private int? GetCurrentUserId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(rawUserId, out var userId) ? userId : null;
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
            MaterialCardListSortOptions.Name => MaterialCardListSortOptions.Name,
            _ => MaterialCardListSortOptions.Code
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

    // �"��"� Grid kolon yonetimi �"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"�

    private static readonly GridColumnDefinition[] MaterialCardGridColumns =
    [
        new() { Key = "MaterialCode",        Label = "Malzeme Kodu" },
        new() { Key = "MaterialName",        Label = "Malzeme Adi" },
        new() { Key = "UnitName",            Label = "Olcu Birimi" },
        new() { Key = "IsActive",            Label = "Durum" },
        new() { Key = "CreatedDate",         Label = "Kayit Tarihi" },
        new() { Key = "ModifiedDate",        Label = "Guncelleme Tarihi" },
    ];

    private static readonly string[] DefaultMaterialCardColumns =
        ["MaterialCode", "MaterialName", "UnitName"];

    private async Task<IReadOnlyCollection<string>> GetMaterialCardVisibleColumnsAsync(CancellationToken ct)
    {
        var userId = GetUserId();
        var cols = await _uiConfigurationService.GetGridColumnPreferencesAsync(userId, "logistics-material-cards", ct);
        return cols.Count > 0 ? cols : DefaultMaterialCardColumns;
    }

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : 0;
    }

    // �"��"� AJAX JSON Endpoint'leri (MaterialCards) �"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"��"�

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
        var units = await _logisticsConfigurationService.GetUnitsAsync(ct);
        var unitNameById = units.ToDictionary(u => u.Id, u => u.Name);

        var allRows = snapshot.Items
            .Select(x => new MaterialCardRowViewModel
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                TypeId = x.TypeId,
                UnitId = x.UnitId,
                UnitName = x.UnitId.HasValue && unitNameById.TryGetValue(x.UnitId.Value, out var n) ? n : null,
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
                MaterialCode = i.Code,
                MaterialName = i.Name,
                unitId = i.UnitId,
                unitName = i.UnitName,
                i.IsActive,
                MaterialTypeId = i.TypeId
            }),
            totalCount,
            totalPages,
            page = currentPage,
            pageSize = listQuery.PageSize,
            visibleColumns
        });
    }

    // NOT: GetMaterialCard MaterialController'a tasindi (rapor �2.3 split).

    // NOT: UpdateFeatureVisibilityJson + UpdateValueAciklamaJson ProductFeatureController'a tasindi (rapor �2.3 split).

    // NOT: SaveMaterialCardJson + DeleteMaterialCardJson MaterialController'a tasindi (rapor �2.3 split).

    [HttpPost]
    public async Task<IActionResult> SaveMaterialCardGridColumns([FromBody] string[] columns, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        await _uiConfigurationService.SaveGridColumnPreferencesAsync(userId, "logistics-material-cards", columns, ct);
        return Ok(new { success = true });
    }

    // -- �l�� Birimi D�n�s�mleri (Stok Karti bazli) --

    [HttpGet]
    public async Task<IActionResult> GetItemUnits(int itemId, CancellationToken ct)
    {
        var items = await _logisticsConfigurationService.GetItemUnitsAsync(itemId, ct);
        var units = await _logisticsConfigurationService.GetUnitsAsync(ct);
        return Json(new
        {
            // Master birim Items.UnitId'de � bu liste sadece alternat birimler (lineNo>=1).
            conversions = items.Where(x => x.LineNo >= 1).Select(x => new { x.LineNo, x.UnitId, x.Multiplier }),
            // Tum birimler donulur � inactive olanlar frontend'de strikethrough/disabled gosterilir
            // ki mevcut secimini koruyup pasif olanlar kullaniciya belli olsun.
            availableUnits = units
                .OrderBy(u => u.IsActive ? 0 : 1)
                .ThenBy(u => u.SortOrder)
                .ThenBy(u => u.Code)
                .Select(u => new { u.Id, unitCode = u.Code, unitName = u.Name, u.IsActive }),
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveItemUnits([FromBody] SaveItemUnitsInput input, CancellationToken ct)
    {
        if (input.ItemId <= 0)
            return Json(new { success = false, message = "Stok karti ID gerekli." });

        var items = (input.Items ?? [])
            .Where(x => x.UnitId > 0 && x.Multiplier > 0)
            .Select(x => new Application.Contracts.SaveItemUnitItem(x.UnitId, x.Multiplier))
            .ToList();

        // Tekrar eden birim kontrol�
        var ids = items.Select(x => x.UnitId).ToList();
        if (ids.Distinct().Count() != ids.Count)
            return Json(new { success = false, message = "Ayni olcu birimi birden fazla tanimlanamaz." });

        await _logisticsConfigurationService.SaveItemUnitsAsync(input.ItemId, items, ct);
        return Json(new { success = true });
    }

    // NOT: GetLocationTypes + SaveLocationType + DeleteLocationType + GetItemLocations + SaveItemLocations LocationController'a tasindi (rapor 2.3 split).

    // NOT: GetStockFeatures + SaveStockFeatures ProductFeatureController'a tasindi (rapor 2.3 split).
}

public sealed record SaveItemUnitsInput(int ItemId, SaveItemUnitLineInput[]? Items);
public sealed record SaveItemUnitLineInput(int UnitId, decimal Multiplier);
public sealed record SaveStockFeaturesInput(int ItemId, SaveStockFeatureItem[]? Items, int[]? FeatureIds);
public sealed record SaveStockFeatureItem(int FeatureId, bool PrintDescriptionInDesign, int[]? AllowedValueIds);

public sealed record SaveItemLocationsInput(int ItemId, SaveItemLocationLineInput[]? Items);
public sealed record SaveItemLocationLineInput(int LocationId, bool IsDefault);

public sealed record SaveLocationTypeInput(int? Id, string? Code, string? Name, int SortOrder, bool IsActive);

public sealed record SaveProductCombinationsRequest(string StockCode, string[]? SelectedCombinations);
