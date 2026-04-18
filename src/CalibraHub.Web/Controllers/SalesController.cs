using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Web.Models.Logistics;
using CalibraHub.Web.Models.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Security.Claims;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class SalesController : Controller
{
    private readonly IDocumentService _quoteService;
    private readonly IFinanceService _financeService;
    private readonly ILogisticsConfigurationService _logisticsService;
    private readonly IUiConfigurationService _uiConfigurationService;
    private readonly IWidgetService _widgetService;
    private readonly IFieldSettingRepository _fieldSettings;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    private static readonly GridColumnDefinition[] DocumentGridColumns =
    [
        new() { Key = "DocumentNumber",  Label = "Teklif No" },
        new() { Key = "DocumentDate",    Label = "Tarih" },
        new() { Key = "ContactName", Label = "Musteri" },
        new() { Key = "GrandTotal",   Label = "Toplam" },
        new() { Key = "Status",       Label = "Durum" },
    ];

    private static readonly string[] DefaultDocumentColumns =
        ["DocumentNumber", "DocumentDate", "ContactName", "GrandTotal", "Status"];

    public SalesController(
        IDocumentService quoteService,
        IFinanceService financeService,
        ILogisticsConfigurationService logisticsService,
        IUiConfigurationService uiConfigurationService,
        IWidgetService widgetService,
        IFieldSettingRepository fieldSettings,
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions dbOptions)
    {
        _quoteService = quoteService;
        _financeService = financeService;
        _logisticsService = logisticsService;
        _uiConfigurationService = uiConfigurationService;
        _widgetService = widgetService;
        _fieldSettings = fieldSettings;
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(dbOptions.Schema) ? "dbo" : dbOptions.Schema.Trim();
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    [HttpGet]
    public async Task<IActionResult> Documents(CancellationToken ct)
    {
        var userId = GetUserId();
        var cols = await _uiConfigurationService.GetGridColumnPreferencesAsync(userId, "sales-quotes", ct);
        var boardConfig = await BuildDocumentsBoardConfigAsync(ct);
        return View(new DocumentsViewModel
        {
            AvailableColumns = DocumentGridColumns,
            VisibleColumns = cols.Count > 0 ? cols : DefaultDocumentColumns,
            BoardConfig = boardConfig,
        });
    }

    // ════════════════════════════════════════════════════════════════
    // BuildDocumentsBoardConfigAsync
    //
    // SmartBoard (CalibraSmartBoard) icin server-side BoardConfig objesi
    // uretir. Malzeme Kartlari ekraninin ayni mimarisi — "Zeki Veri, Aptal
    // Bilesen": React bileseni sadece JSON'u cizer, is mantigi server'da.
    //
    // Widget JSON kontrati: { id, type:"data", dataType, label, value, detail, color }
    // ════════════════════════════════════════════════════════════════
    private async Task<object> BuildDocumentsBoardConfigAsync(CancellationToken ct)
    {
        var quotes = await _quoteService.GetQuotesAsync(search: null, status: null, ct);
        var trCulture = CultureInfo.GetCultureInfo("tr-TR");

        // ── Master widget sablonu (SmartBoardConfigPanel icin) ──
        // Sadece SALES_QUOTE_EDIT form kodundaki admin-tanimli dinamik widget'lar eklenir.
        // Sistem alanlari (tutar, durum vb.) widget olarak tanimlanmadikca listede gorünmez.
        var masterWidgets = new List<object>();
        var sqSchema = await _widgetService.GetFormSchemaByCodeAsync("SALES_QUOTE_EDIT", ct);
        if (sqSchema != null)
        {
            foreach (var w in sqSchema.Widgets.Where(w => w.IsActive
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

        // Batch widget degerleri — tum teklifler icin tek sorgu
        var recordIds = quotes.Select(q => q.Id.ToString()).ToArray();
        var batchWidgets = masterWidgets.Count > 0 && recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("SALES_QUOTE_EDIT", recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        var entities = new List<object>();
        foreach (var quote in quotes)
        {
            var widgets = new List<object>();

            // ── Sistem widget'lari ──
            widgets.Add(new { id = "w_tutar", type = "data", dataType = "currency", label = "Toplam Tutar",
                value = quote.GrandTotal.ToString("N2", trCulture), detail = quote.Currency ?? "TRY",
                color = "blue" });
            widgets.Add(new { id = "w_durum", type = "data", dataType = "text", label = "Durum",
                value = TranslateStatus(quote.Status), detail = (string?)null,
                color = StatusColor(quote.Status) });
            widgets.Add(new { id = "w_kalem", type = "data", dataType = "numeric", label = "Kalem Sayisi",
                value = quote.LineCount.ToString(CultureInfo.InvariantCulture), detail = "kalem",
                color = "slate" });
            widgets.Add(new { id = "w_tarih", type = "data", dataType = "date", label = "Tarih",
                value = quote.DocumentDate.ToString("dd.MM.yyyy", trCulture), detail = (string?)null,
                color = "slate" });
            if (quote.ValidUntil.HasValue)
            {
                var isFuture = quote.ValidUntil.Value.Date >= DateTime.Today;
                widgets.Add(new { id = "w_gecerlilik", type = "data", dataType = "date", label = "Gecerlilik",
                    value = quote.ValidUntil.Value.ToString("dd.MM.yyyy", trCulture), detail = (string?)null,
                    color = isFuture ? "emerald" : "rose" });
            }

            // ── Dinamik widget degerleri (WidgetTra) ──
            var recordId = quote.Id.ToString();
            if (batchWidgets.TryGetValue(recordId, out var renderDtos))
            {
                foreach (var w in renderDtos)
                {
                    widgets.Add(new {
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
                    });
                }
            }

            entities.Add(new
            {
                id = quote.Id,
                title = string.IsNullOrWhiteSpace(quote.ContactName) ? "(musterisiz)" : quote.ContactName,
                subtitle = quote.DocumentNumber ?? string.Empty,
                description = string.Empty,
                imageUrl = (string?)null,
                statusBadge = (object?)null,
                widgets,
                primaryAction = new
                {
                    label = "Duzenle",
                    icon = "Edit",
                    url = $"/Sales/DocumentEdit?id={quote.Id}",
                },
                secondaryAction = new
                {
                    label = "Sil",
                    icon = "Trash2",
                    apiUrl = $"/Sales/DeleteDocumentJson?id={quote.Id}",
                    confirm = $"Bu teklifi silmek istediginizden emin misiniz? ({quote.DocumentNumber})",
                },
            });
        }

        return new
        {
            boardKey = "sales-quotes",
            title = "Satis Teklifleri",
            subtitle = $"{entities.Count} teklif",
            icon = "FileText",
            iconColor = "indigo",
            searchPlaceholder = "Hizli ara... (teklif no, musteri)",
            emptyText = "Henuz teklif olusturulmamis",
            actions = new[]
            {
                new
                {
                    id = "new",
                    label = "Yeni Teklif",
                    icon = "Plus",
                    variant = "primary",
                    url = "/Sales/DocumentEdit",
                },
            },
            masterWidgets,
            entities,
        };
    }

    /// <summary>Quote status enum → Turkce etiket</summary>
    private static string TranslateStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "draft"     => "Taslak",
        "sent"      => "Gonderildi",
        "approved"  => "Onaylandi",
        "rejected"  => "Reddedildi",
        "revised"   => "Revize",
        "cancelled" => "Iptal",
        _ => status ?? "-",
    };

    /// <summary>Quote status → SmartBoard palette rengi</summary>
    private static string StatusColor(string? status) => status?.ToLowerInvariant() switch
    {
        "draft"     => "amber",
        "sent"      => "blue",
        "approved"  => "emerald",
        "rejected"  => "rose",
        "revised"   => "violet",
        "cancelled" => "slate",
        _ => "slate",
    };

    [HttpPost]
    public async Task<IActionResult> SaveDocumentGridColumns([FromBody] string[] columns, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await _uiConfigurationService.SaveGridColumnPreferencesAsync(userId, "sales-quotes", columns, ct);
        return Ok(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> DocumentEdit(int? id, CancellationToken ct)
    {
        // Satır grid kolonlarına rehber binding'lerini runtime'da inject et
        var bindings = await _fieldSettings.GetGuideBindingsForFormAsync("SALES_QUOTE_LINES", ct);
        var lineGridConfig = BuildDocumentLineGridConfig(bindings);
        var jsonOpts = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        var vm = new DocumentEditViewModel
        {
            DocumentId = id,
            LineGridConfigJson = System.Text.Json.JsonSerializer.Serialize(lineGridConfig, jsonOpts),
        };

        return View("DocumentEdit", vm);
    }

    // ════════════════════════════════════════════════════════════════
    // BuildDocumentLineGridConfig
    //
    // Satis teklifi kalem grid'i (CalibraLineItemsGrid React bileseni) icin
    // server-side kolon ve meta JSON'i hazirlar. "Aptal Bilesen, Zeki Veri":
    // React ne kolon ismi ne kolon sirasi bilmez — hepsi buradan gelir.
    //
    // Kolon tipi sozlugu (React tarafinda destekli):
    //   text, text-lookup, select, number, currency, percent, date
    // Computed hucreler: formula string'i gelir, React guvenli evaluator ile hesaplar.
    // ════════════════════════════════════════════════════════════════
    private object BuildDocumentLineGridConfig(
        IReadOnlyCollection<FieldGuideBindingDto>? bindings = null)
    {
        // Binding sözlüğü: fieldKey → (guideCode, isRequired, filterJson)
        var bindingMap = (bindings ?? [])
            .ToDictionary(b => b.FieldKey, b => b, StringComparer.OrdinalIgnoreCase);

        // materialCode kolonu için binding varsa guide ekle, yoksa lookupUrl fallback
        bindingMap.TryGetValue("materialCode", out var matBinding);

        return new
        {
            schemaVersion = "v1",
            columns = new object[]
            {
                new
                {
                    key = "materialCode",
                    label = "Malzeme Kodu",
                    type = "text-lookup",
                    // Binding varsa guide mode, yoksa doğrudan URL ile inline dropdown
                    guideCode      = matBinding?.GuideCode,
                    filterJson     = matBinding?.FilterJson,
                    formCode       = "SALES_QUOTE_LINES",
                    formatJson     = matBinding?.FormatJson,
                    lookupUrl      = matBinding == null ? "/Sales/GetMaterials" : (string?)null,
                    lookupValueKey = "materialCode",
                    lookupLabelKey = "materialName",
                    lookupFillMap = new Dictionary<string, string>
                    {
                        ["materialName"]      = "MaterialName",
                        ["stockCardId"]       = "Id",
                        ["trackCombinations"] = "TrackCombinations",
                    },
                    width    = 220,
                    required = matBinding?.IsRequired ?? false,
                    align    = "left",
                    icon     = "Hash",
                },
                new
                {
                    key = "materialName",
                    label = "Malzeme Adi",
                    type = "text",
                    width = "flex",
                    @readonly = true,
                    align = "left",
                    icon = "FileText",
                },
                new
                {
                    key = "combinationCode",
                    label = "Kombinasyon",
                    type = "combination-lookup",
                    width = 140,
                    align = "center",
                    icon = "CircleDot",
                    // Sadece row.trackCombinations=true iken aktif olur (client-side kontrol)
                    visibleWhenKey = "trackCombinations",
                },
                new
                {
                    key = "unitName",
                    label = "Birim",
                    type = "select",
                    optionsUrl = "/Sales/GetMaterialUnits?materialCode={materialCode}",
                    optionsValueKey = "code",
                    optionsLabelKey = "name",
                    // Secenekler geldiginde hucre bos ise ilk option (master birim)
                    // otomatik atanir. Kullanici farkli birim secmek isterse degistirebilir.
                    autoSelectFirst = true,
                    width = 90,
                    align = "center",
                    icon = "Ruler",
                },
                new
                {
                    key = "quantity",
                    label = "Miktar",
                    type = "number",
                    width = 100,
                    precision = 2,
                    min = 0,
                    align = "right",
                    icon = "Sigma",
                },
                new
                {
                    key = "unitPrice",
                    label = "Birim Fiyat",
                    type = "currency",
                    width = 130,
                    precision = 2,
                    min = 0,
                    align = "right",
                    icon = "DollarSign",
                },
                new
                {
                    key = "discountRate",
                    label = "Iskonto %",
                    type = "percent",
                    width = 100,
                    precision = 2,
                    min = 0,
                    max = 100,
                    align = "right",
                    icon = "Percent",
                },
                new
                {
                    key = "lineTotal",
                    label = "Satir Toplami",
                    type = "currency",
                    width = 140,
                    computed = true,
                    formula = "quantity * unitPrice * (1 - (discountRate / 100))",
                    align = "right",
                    icon = "Calculator",
                },
                new
                {
                    key = "notes",
                    label = "Not",
                    type = "text",
                    placement = "row-below",
                    align = "left",
                    icon = "StickyNote",
                },
            },
            rows = System.Array.Empty<object>(),     // Baslangic bos — mevcut teklif yuklenirken JS bridge doldurur
            labels = new
            {
                addRow = "Yeni Kalem",
                deleteConfirm = "Bu kalemi silmek istediginizden emin misiniz?",
                totalLabel = "Toplam",
                subtotalLabel = "Ara Toplam",
                emptyText = "Henuz kalem eklenmemis",
            },
            footer = new
            {
                showSubtotal = true,
                subtotalColumns = new[] { "lineTotal" },
            },
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetDocuments(string? search, string? status, CancellationToken ct)
    {
        var quotes = await _quoteService.GetQuotesAsync(search, status, ct);
        return Json(quotes);
    }

    [HttpGet]
    public async Task<IActionResult> GetQuote(int id, CancellationToken ct)
    {
        var quote = await _quoteService.GetQuoteByIdAsync(id, ct);
        if (quote == null) return NotFound();
        var lines = await _quoteService.GetQuoteLinesAsync(id, ct);
        return Json(new { quote, lines });
    }

    [HttpGet]
    public async Task<IActionResult> GetNextQuoteNumber(CancellationToken ct)
    {
        var number = await _quoteService.GetNextDocumentNumberAsync(ct);
        return Json(new { number });
    }

    [HttpGet]
    public async Task<IActionResult> GetCustomers(CancellationToken ct)
    {
        var accounts = await _financeService.GetContactsAsync(null, null, ct);
        return Json(accounts);
    }

    [HttpGet]
    public async Task<IActionResult> GetMaterials(CancellationToken ct)
    {
        var snapshot = await _logisticsService.GetSnapshotAsync(ct);
        var materials = snapshot.Items
            .Where(x => x.IsActive)
            .Select(x => new { x.Id, x.MaterialCode, x.MaterialName, x.TaxRate, x.TrackCombinations })
            .OrderBy(x => x.MaterialCode)
            .ToArray();
        return Json(materials);
    }

    [HttpGet]
    public async Task<IActionResult> GetMaterialUnit(string materialCode, CancellationToken ct)
    {
        // Geriye donuk uyumluluk — sadece master birimi doner
        if (string.IsNullOrWhiteSpace(materialCode))
            return Json(new { unitCode = "", unitName = "" });

        var widgetValues = await LoadMaterialWidgetValuesAsync(materialCode, ct);
        var unitCode = widgetValues.GetValueOrDefault("unit_name") ?? "";
        var unitName = "";
        if (!string.IsNullOrWhiteSpace(unitCode))
        {
            var allUnits = await _logisticsService.GetUnitsAsync(ct);
            var unit = allUnits.FirstOrDefault(u => string.Equals(u.UnitCode, unitCode, StringComparison.OrdinalIgnoreCase));
            unitName = unit?.UnitName ?? unitCode;
        }
        return Json(new { unitCode, unitName });
    }

    [HttpGet]
    public async Task<IActionResult> GetMaterialUnits(string materialCode, CancellationToken ct)
    {
        var units = await GetMaterialUnitsInternal(materialCode, ct);
        return Json(units);
    }

    private async Task<List<object>> GetMaterialUnitsInternal(string materialCode, CancellationToken ct)
    {
        var result = new List<object>();
        if (string.IsNullOrWhiteSpace(materialCode)) return result;

        // Tum olcu birimi tanimlari (kod→isim lookup)
        var allUnits = await _logisticsService.GetUnitsAsync(ct);
        var unitNameLookup = allUnits
            .Where(u => u.IsActive)
            .ToDictionary(u => u.UnitCode.Trim().ToUpperInvariant(), u => u.UnitName, StringComparer.OrdinalIgnoreCase);

        string Resolve(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return code;
            return unitNameLookup.TryGetValue(code.Trim().ToUpperInvariant(), out var n) ? n : code;
        }

        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Tum birimleri yeni EAV widget'lardan al (WidgetMas/WidgetTra — form ITEMS).
        var widgetValues = await LoadMaterialWidgetValuesAsync(materialCode, ct);

        // 1) Master birim
        var masterCode = widgetValues.GetValueOrDefault("unit_name") ?? "";
        if (!string.IsNullOrWhiteSpace(masterCode) && usedCodes.Add(masterCode))
            result.Add(new { code = masterCode, name = Resolve(masterCode) });

        // 2) Satin alma birimi
        var purchaseCode = widgetValues.GetValueOrDefault("purchase_unit_name") ?? "";
        if (!string.IsNullOrWhiteSpace(purchaseCode) && usedCodes.Add(purchaseCode))
            result.Add(new { code = purchaseCode, name = Resolve(purchaseCode) });

        // 3) ek birimler: unit_conv_1_code .. unit_conv_5_code
        for (var i = 1; i <= 5; i++)
        {
            var convCode = widgetValues.GetValueOrDefault($"unit_conv_{i}_code") ?? "";
            if (!string.IsNullOrWhiteSpace(convCode) && usedCodes.Add(convCode))
                result.Add(new { code = convCode, name = Resolve(convCode) });
        }

        // 4) stock_unit_conversions tablosundan da birimleri topla — stok kartinin
        // "Birim Donusumleri" ekraninda eklenen birimler burada saklanir.
        // ItemId'yi materials snapshot'tan cozeriz.
        var snapshot = await _logisticsService.GetSnapshotAsync(ct);
        var stockCard = snapshot.Items.FirstOrDefault(x =>
            string.Equals(x.MaterialCode, materialCode, StringComparison.OrdinalIgnoreCase));
        if (stockCard != null)
        {
            var conversions = await _logisticsService.GetStockUnitConversionsAsync(stockCard.Id, ct);
            foreach (var cv in conversions)
            {
                if (!string.IsNullOrWhiteSpace(cv.UnitCode) && usedCodes.Add(cv.UnitCode))
                    result.Add(new { code = cv.UnitCode, name = Resolve(cv.UnitCode) });
            }
        }

        // Malzeme kartinda birim tanimli degilse — tum sistem birimlerini
        // doldurmuyoruz (kullanici kafasi karisiyor). Bos liste doneriz;
        // kullanici malzeme kartini duzenleyip birim tanimlamali.
        return result;
    }

    /// <summary>
    /// ITEMS form'unun verilen materialCode kaydi icin tum widget'lari okur ve
    /// widgetCode → string value sozlugu doner. DataType ne olursa olsun deger
    /// string'e cevrilir (multi-select JSON array dahil).
    /// </summary>
    private async Task<Dictionary<string, string?>> LoadMaterialWidgetValuesAsync(
        string materialCode, CancellationToken ct)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(materialCode)) return map;

        var record = await _widgetService.GetRecordByCodeAsync("ITEMS", materialCode, ct);
        if (record == null) return map;

        foreach (var w in record.Widgets)
        {
            if (string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)) continue;
            map[w.WidgetId] = w.Value switch
            {
                null => null,
                string s => s,
                bool b => b ? "true" : "false",
                DateTime dt => dt.ToString("yyyy-MM-dd"),
                _ => Convert.ToString(w.Value, System.Globalization.CultureInfo.InvariantCulture)
            };
        }
        return map;
    }

    [HttpGet]
    public async Task<IActionResult> GetMeasureUnits(CancellationToken ct)
    {
        var units = await _logisticsService.GetUnitsAsync(ct);
        return Json(units.Where(u => u.IsActive).Select(u => new { code = u.UnitCode, name = u.UnitName }));
    }

    [HttpGet]
    public async Task<IActionResult> GetCombinations(string materialCode, CancellationToken ct)
    {
        var combos = await _logisticsService.GetCombinationsForLookupAsync(materialCode, ct);
        return Json(combos.Select(c => new
        {
            code = c.Code,
            name = c.Name,
            features = c.FeatureValues.Select(fv => new { feature = fv.Feature, value = fv.Value })
        }));
    }

    [HttpGet]
    public async Task<IActionResult> GetMaterialFeatures(string materialCode, CancellationToken ct)
    {
        var snapshot = await _logisticsService.GetProductConfigurationSnapshotAsync(ct);
        // Bu malzemeye bagli ozellik ID'lerini bul
        var linkedFeatureIds = snapshot.FeatureStockLinks
            .Where(l => string.Equals(l.StockCode, materialCode, StringComparison.OrdinalIgnoreCase))
            .Select(l => l.FeatureId)
            .ToHashSet();

        var features = new List<object>();
        foreach (var feature in snapshot.Features.Where(f => linkedFeatureIds.Contains(f.Id) && f.IsActive))
        {
            var values = snapshot.Values
                .Where(v => v.FeatureId == feature.Id && v.IsActive)
                .Select(v => new { id = v.Id, code = v.Code, name = v.Description })
                .ToArray();
            features.Add(new { featureId = feature.Id, featureName = feature.Name, featureCode = feature.Code, values });
        }
        return Json(features);
    }

    /// <summary>
    /// Satış teklifi satırı için yeni kombinasyon çöz veya oluştur.
    /// Aynı özellik-değer setine sahip mevcut CONFIG varsa onu döner (matched=true);
    /// yoksa yeni bir CONFIG kaydı üretilir (matched=false).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ResolveOrCreateCombination(
        [FromBody] ResolveCombinationRequest request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.MaterialCode))
            return Json(new { success = false, message = "Malzeme kodu zorunludur." });
        if (request.Selections == null || request.Selections.Count == 0)
            return Json(new { success = false, message = "En az bir özellik değeri seçmelisiniz." });

        try
        {
            var result = await _logisticsService.ResolveOrCreateCombinationAsync(request, ct);
            return Json(new
            {
                success = true,
                matched = result.Matched,
                configId = result.ConfigId,
                code = result.Code,
                name = result.Name,
            });
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

    [HttpPost]
    public async Task<IActionResult> SaveDocument([FromBody] SaveDocumentRequest request, CancellationToken ct)
    {
        var userName = User.FindFirstValue(ClaimTypes.Name) ?? "system";
        var (success, error, quote) = await _quoteService.SaveQuoteAsync(request, userName, ct);
        if (!success) return Json(new { success = false, message = error });
        return Json(new { success = true, quote });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteDocument([FromBody] DeleteQuoteBody body, CancellationToken ct)
    {
        await _quoteService.DeleteQuoteAsync(body.Id, ct);
        return Json(new { success = true });
    }

    /// <summary>
    /// SmartCard-uyumlu delete endpoint'i — query string ile id bekler.
    /// SmartCard.jsx secondaryAction.apiUrl body'siz POST gonderdiği icin
    /// mevcut DeleteDocument (body binding) kullanilamaz. Bu wrapper ayni
    /// servisi cagirir ve ayni cevabi doner (MaterialCards DeleteMaterialCardJson
    /// pattern'i ile ayni).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> DeleteDocumentJson(int id, CancellationToken ct)
    {
        try
        {
            await _quoteService.DeleteQuoteAsync(id, ct);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ChangeQuoteStatus([FromBody] ChangeStatusBody body, CancellationToken ct)
    {
        var (success, error) = await _quoteService.ChangeStatusAsync(body.Id, body.Status, ct);
        if (!success) return Json(new { success = false, message = error });
        return Json(new { success = true });
    }

    // ════════════════════════════════════════════════════════════════
    // Ekli Dosyalar — sales_quote_attachments
    // Raw ADO.NET: tablo sadece bu controller tarafindan okunuyor,
    // service/repository layer acmaya degmeyecek kadar kucuk bir yuzey.
    // ════════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> GetQuoteAttachments(Guid quoteId, CancellationToken ct)
    {
        var list = new List<object>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[file_name],[mime_type],[file_size],[uploaded_at]
            FROM [{_schema}].[sales_quote_attachments]
            WHERE [document_id] = @DocumentId AND [is_active] = 1
            ORDER BY [uploaded_at] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@DocumentId", quoteId));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new
            {
                id = r.GetGuid(0),
                fileName = r.GetString(1),
                mimeType = r.IsDBNull(2) ? null : r.GetString(2),
                fileSize = r.GetInt64(3),
                uploadedAt = r.GetDateTime(4),
            });
        }
        return Json(list);
    }

    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB per dosya
    public async Task<IActionResult> UploadQuoteAttachment(
        [FromForm] Guid quoteId,
        [FromForm] List<IFormFile> files,
        CancellationToken ct)
    {
        if (files == null || files.Count == 0)
            return BadRequest(new { success = false, message = "Dosya yok." });

        var user = User.Identity?.Name ?? "unknown";
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        foreach (var file in files)
        {
            if (file.Length <= 0) continue;
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO [{_schema}].[sales_quote_attachments]
                    ([id],[document_id],[file_name],[mime_type],[file_size],[content],[uploaded_by],[uploaded_at],[is_active])
                VALUES (@Id,@DocumentId,@FileName,@Mime,@Size,@Content,@User,SYSUTCDATETIME(),1);
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", Guid.NewGuid()));
            cmd.Parameters.Add(new SqlParameter("@DocumentId", quoteId));
            cmd.Parameters.Add(new SqlParameter("@FileName", file.FileName ?? "file"));
            cmd.Parameters.Add(new SqlParameter("@Mime", (object?)file.ContentType ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Size", (long)bytes.Length));
            cmd.Parameters.Add(new SqlParameter("@Content", bytes));
            cmd.Parameters.Add(new SqlParameter("@User", user));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        return Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadQuoteAttachment(Guid id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [file_name],[mime_type],[content]
            FROM [{_schema}].[sales_quote_attachments]
            WHERE [id] = @Id AND [is_active] = 1;
            """;
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return NotFound();
        var fileName = r.GetString(0);
        var mime = r.IsDBNull(1) ? "application/octet-stream" : r.GetString(1);
        var content = (byte[])r[2];
        return File(content, mime, fileName);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteQuoteAttachment([FromBody] DeleteQuoteBody body, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE [{_schema}].[sales_quote_attachments] SET [is_active] = 0 WHERE [id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", body.Id));
        await cmd.ExecuteNonQueryAsync(ct);
        return Json(new { success = true });
    }
}

public sealed record DeleteQuoteBody(int Id);
public sealed record ChangeStatusBody(int Id, string Status);
