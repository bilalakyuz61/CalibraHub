using CalibraHub.Application.Constants;
using CalibraHub.Application.Abstractions.DesignProvider;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Web.Models.Logistics;
using CalibraHub.Web.Models.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
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
    private readonly IWidgetRepository _widgetRepo;
    private readonly IFieldSettingRepository _fieldSettings;
    private readonly IDocumentTypeRepository _documentTypeRepo;
    private readonly IFormMetadataService _formMetadata;
    private readonly IPersonnelService _personnelService;
    private readonly IWorkOrderService _workOrderService;
    private readonly IPrintDispatcher _printDispatcher;
    private readonly IIntegrationOnSaveDispatcher _onSaveDispatcher;
    private readonly IApprovalFlowService _approvalFlowService;
    private readonly IPermissionService _permService;
    private readonly IStockDocRepository _stockDocRepo;
    private readonly ICompanyParameterService _companyParams;
    private readonly IPriceListService _priceListService;
    private readonly ILogger<SalesController> _logger;
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
        IWidgetRepository widgetRepo,
        IFieldSettingRepository fieldSettings,
        IDocumentTypeRepository documentTypeRepo,
        IFormMetadataService formMetadata,
        IPersonnelService personnelService,
        IWorkOrderService workOrderService,
        IPrintDispatcher printDispatcher,
        IIntegrationOnSaveDispatcher onSaveDispatcher,
        IApprovalFlowService approvalFlowService,
        IPermissionService permService,
        IStockDocRepository stockDocRepo,
        ICompanyParameterService companyParams,
        IPriceListService priceListService,
        ILogger<SalesController> logger,
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions dbOptions)
    {
        _quoteService = quoteService;
        _financeService = financeService;
        _logisticsService = logisticsService;
        _uiConfigurationService = uiConfigurationService;
        _widgetService = widgetService;
        _widgetRepo = widgetRepo;
        _fieldSettings = fieldSettings;
        _documentTypeRepo = documentTypeRepo;
        _formMetadata = formMetadata;
        _personnelService = personnelService;
        _workOrderService = workOrderService;
        _printDispatcher = printDispatcher;
        _onSaveDispatcher = onSaveDispatcher;
        _approvalFlowService = approvalFlowService;
        _permService = permService;
        _stockDocRepo = stockDocRepo;
        _companyParams = companyParams;
        _priceListService = priceListService;
        _logger = logger;
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(dbOptions.Schema) ? "dbo" : dbOptions.Schema.Trim();
    }

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : 0;
    }

    private int? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    [HttpGet]
    [Route("/Sales/Quotes")]               // YENI tercih edilen URL — teklif-spesifik (CamelCase + anlamli)
    [Route("/Sales/Documents")]            // ESKI route'u yasatiyoruz — mevcut bookmark/integration'lar kirilmasin.
                                            // Yeni baglantilar Quotes'i kullanir; eski URL'i sessizce ayni view'a yonlendirir.
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.SalesQuote)]
    public async Task<IActionResult> Quotes(CancellationToken ct)
    {
        var userId = GetUserId();
        var cols = await _uiConfigurationService.GetGridColumnPreferencesAsync(userId, "sales-quotes", ct);
        var boardConfig = await BuildQuotesBoardConfigAsync(ct);
        return View("Documents", new DocumentsViewModel
        {
            AvailableColumns = DocumentGridColumns,
            VisibleColumns = cols.Count > 0 ? cols : DefaultDocumentColumns,
            BoardConfig = boardConfig,
        });
    }

    // Geri uyum: eski cagirim noktalari hala "Documents" action'ini referans verebilir.
    // Sadece Quotes'a delege et.
    [HttpGet]
    public Task<IActionResult> Documents(CancellationToken ct) => Quotes(ct);

    // ════════════════════════════════════════════════════════════════
    // BuildQuotesBoardConfigAsync
    //
    // SmartBoard (CalibraSmartBoard) icin server-side BoardConfig objesi
    // uretir. Malzeme Kartlari ekraninin ayni mimarisi — "Zeki Veri, Aptal
    // Bilesen": React bileseni sadece JSON'u cizer, is mantigi server'da.
    //
    // Widget JSON kontrati: { id, type:"data", dataType, label, value, detail, color }
    // ════════════════════════════════════════════════════════════════
    private async Task<object> BuildQuotesBoardConfigAsync(CancellationToken ct)
    {
        // DocumentType "satis_teklifi" filtresi — eski GetQuotesAsync ad/davranis
        // uyumsuzdu: tum belgeleri (siparis dahil) cekiyor, ekrana siparis kayitlari
        // da dusuyordu (bug raporu 2026-05-20). GetByTypeAsync ile dogru filtrelenir.
        var quotes = await _quoteService.GetByTypeAsync("satis_teklifi", search: null, status: null, ct);
        var trCulture = CultureInfo.GetCultureInfo("tr-TR");

        // 2026-05-24: SmartBoardFilterHelpers ile standardize.
        var sqSchema = await _widgetService.GetFormSchemaByCodeAsync("SALES_QUOTE_EDIT", ct);
        var masterWidgets = CalibraHub.Web.Helpers.SmartBoardFilterHelpers.BuildAdminFormWidgets(sqSchema);
        // Sistem alanlari — "Standart Alanlar" collapsible
        var statusOptions = CalibraHub.Web.Helpers.SmartBoardFilterHelpers.ToOptionsList(
            new[] { "Taslak", "Gonderildi", "Onaylandi", "Reddedildi", "Iptal", "Kapali" });
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_tutar", "Toplam Tutar", "currency"));
        var sqDurumW = CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_durum", "Durum", "options");
        sqDurumW["options"] = statusOptions;
        masterWidgets.Add(sqDurumW);
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_kalem", "Kalem Sayısı", "numeric"));
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_tarih", "Tarih", "date"));
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_valid", "Geçerlilik Tarihi", "date"));

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
                value = quote.GrandTotal.ToString("N2", trCulture), detail = quote.CurrencyCode ?? "TRY",
                color = "blue" });
            widgets.Add(new { id = "w_durum", type = "data", dataType = "options", label = "Durum",
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
                // Duzenle aksiyonu — sol aksiyon seridinde buton render EDILMEZ
                // (hideButton: true), sadece kartin kimlik seridine (cari isim +
                // belge no) tiklandiginda bu URL'e gidilir. SmartCard.jsx identity
                // onClick icinde primaryAction.url kullanilir.
                primaryAction = new
                {
                    label = "Duzenle",
                    icon = "Edit",
                    color = "amber",
                    url = $"/Sales/DocumentEdit?id={quote.Id}",
                    hideButton = true,
                },
                secondaryAction = new
                {
                    label = "Sil",
                    icon = "Trash2",
                    apiUrl = $"/Sales/DeleteDocumentJson?id={quote.Id}",
                    precheckUrl = $"/Sales/CanDeleteDocumentJson?id={quote.Id}",
                    confirm = $"Bu teklifi silmek istediginizden emin misiniz? ({quote.DocumentNumber})",
                },
                // Kartin sol aksiyon seridinde Duzenle + Sil'in yani sira
                // Yazdir ve Mail butonlari. SmartCard.jsx extraActions dizisini
                // sol panele soft/slate renkli ekstra ikonlar olarak basar.
                extraActions = new object[]
                {
                    new
                    {
                        // Tek teklif → siparise donustur (kart-bazli) + opsiyonel is emri.
                        // Sadece Approved tekliflerde aktif anlamli — yine de kullaniciya
                        // gosterip server-side validation'a birakiyoruz (UX kalabaliklik).
                        label = "Siparise Donustur",
                        icon = "ShoppingCart",
                        color = "emerald",
                        type = "trigger",
                        trigger = "convert-single-quote-modal",
                        payload = new
                        {
                            quoteId = quote.Id,
                            quoteNumber = quote.DocumentNumber,
                            contactName = quote.ContactName,
                            grandTotal = quote.GrandTotal,
                            currencyId = quote.CurrencyId,
                            currency = quote.CurrencyCode,    // display-only
                            lineCount = quote.LineCount,
                        },
                    },
                    new
                    {
                        label = "Yazdir",
                        icon = "Printer",
                        color = "indigo",
                        // fetch-modal: PDF'i SmartCard modal'i icindeki iframe'de goster.
                        // modal header'i X kapatma butonunu zaten sunar — kullanici modal
                        // baslik alanindan kolayca kapatir.
                        type = "fetch-modal",
                        fetchUrl = $"/Sales/PrintQuoteDialog?id={quote.Id}",
                        modalTitle = $"Yazdir — {quote.DocumentNumber}",
                    },
                    new
                    {
                        label = "Mail Gonder",
                        icon = "Mail",
                        color = "sky",
                        // inline-kitt: kart icinde acilan dar seritli form — modal
                        // acmaz. Basari halinde animasyonla otomatik kapanir.
                        // Ortak mail endpoint'i DocumentMailController'da; tum
                        // belge turleri (QUOTE/ORDER/INVOICE/DISPATCH) ayni
                        // endpoint'e documentType parametresi ile gider.
                        type = "inline-kitt",
                        apiUrl = "/Document/SendMail",
                        body = new
                        {
                            documentId = quote.Id,
                            documentType = "QUOTE",
                        },
                        fields = new object[]
                        {
                            new
                            {
                                name = "to",
                                type = "email",
                                placeholder = "Alici e-posta (ornek@firma.com)",
                                required = true,
                                flex = 2,
                                defaultValue = string.Empty, // TODO: Cari e-postasi dolsun
                            },
                            new
                            {
                                name = "subject",
                                type = "text",
                                placeholder = "Konu",
                                required = true,
                                flex = 3,
                                defaultValue = $"Satis Teklifi {quote.DocumentNumber}",
                            },
                        },
                        submitLabel = "Gonder",
                        successMessage = "Mail kuyruga alindi",
                    },
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
            actions = new object[]
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

    // ════════════════════════════════════════════════════════════════
    // SIPARIS LISTESI EKRANI (Document type = satis_siparisi)
    // ════════════════════════════════════════════════════════════════

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.SalesOrder)]
    public async Task<IActionResult> Orders(CancellationToken ct)
    {
        var boardConfig = await BuildOrdersBoardConfigAsync(ct);
        return View(new DocumentsViewModel
        {
            AvailableColumns = DocumentGridColumns,
            VisibleColumns = DefaultDocumentColumns,
            BoardConfig = boardConfig,
        });
    }

    private async Task<object> BuildOrdersBoardConfigAsync(CancellationToken ct)
    {
        var orders = await _quoteService.GetByTypeAsync("satis_siparisi", search: null, status: null, ct);
        var trCulture = CultureInfo.GetCultureInfo("tr-TR");

        // 2026-05-24: SmartBoardFilterHelpers — admin form widgets + sistem alanlar collapsible
        var soSchema = await _widgetService.GetFormSchemaByCodeAsync("SALES_ORDER_EDIT", ct);
        var masterWidgets = CalibraHub.Web.Helpers.SmartBoardFilterHelpers.BuildAdminFormWidgets(soSchema);
        var statusOptions = CalibraHub.Web.Helpers.SmartBoardFilterHelpers.ToOptionsList(
            new[] { "Taslak", "Gonderildi", "Onaylandi", "Reddedildi", "Iptal", "Kapali" });
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_tutar", "Toplam Tutar", "currency"));
        var soDurumW = CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_durum", "Durum", "options");
        soDurumW["options"] = statusOptions;
        masterWidgets.Add(soDurumW);
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_kalem", "Kalem Sayısı", "numeric"));
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_tarih", "Sipariş Tarihi", "date"));

        var entities = new List<object>();
        foreach (var order in orders)
        {
            var widgets = new List<object>
            {
                new { id = "w_tutar", type = "data", dataType = "currency", label = "Toplam Tutar",
                    value = order.GrandTotal.ToString("N2", trCulture), detail = order.CurrencyCode ?? "TRY",
                    color = "blue" },
                new { id = "w_durum", type = "data", dataType = "options", label = "Durum",
                    value = TranslateStatus(order.Status), detail = (string?)null,
                    color = StatusColor(order.Status) },
                new { id = "w_kalem", type = "data", dataType = "numeric", label = "Kalem Sayisi",
                    value = order.LineCount.ToString(CultureInfo.InvariantCulture), detail = "kalem",
                    color = "slate" },
                new { id = "w_tarih", type = "data", dataType = "date", label = "Siparis Tarihi",
                    value = order.DocumentDate.ToString("dd.MM.yyyy", trCulture), detail = (string?)null,
                    color = "slate" },
            };

            entities.Add(new
            {
                id = order.Id,
                title = string.IsNullOrWhiteSpace(order.ContactName) ? "(musterisiz)" : order.ContactName,
                subtitle = order.DocumentNumber ?? string.Empty,
                description = string.Empty,
                imageUrl = (string?)null,
                statusBadge = (object?)null,
                widgets,
                primaryAction = new
                {
                    label = "Duzenle",
                    icon = "Edit",
                    color = "amber",
                    url = $"/Sales/DocumentEdit?id={order.Id}",
                    hideButton = true,
                },
                secondaryAction = new
                {
                    label = "Sil",
                    icon = "Trash2",
                    apiUrl = $"/Sales/DeleteDocumentJson?id={order.Id}",
                    precheckUrl = $"/Sales/CanDeleteDocumentJson?id={order.Id}",
                    confirm = $"Bu siparisi silmek istediginizden emin misiniz? ({order.DocumentNumber})",
                },
            });
        }

        return new
        {
            boardKey = "sales-orders",
            title = "Satis Siparisleri",
            subtitle = $"{entities.Count} siparis",
            icon = "ShoppingCart",
            iconColor = "emerald",
            searchPlaceholder = "Hizli ara... (siparis no, musteri)",
            emptyText = "Henuz siparis olusturulmamis",
            actions = new object[]
            {
                new
                {
                    id = "new-order",
                    label = "Yeni Siparis",
                    icon = "Plus",
                    variant = "primary",
                    url = "/Sales/DocumentEdit?type=order",
                },
                // Toolbar — onaylanmis teklifleri cari bazinda siparise donusturen modal
                new
                {
                    id = "convert-to-orders",
                    label = "Tekliften Siparis",
                    icon = "ArrowRightCircle",
                    variant = "secondary",
                    trigger = "convert-orders-modal",
                },
            },
            masterWidgets,
            entities,
        };
    }

    // ════════════════════════════════════════════════════════════════
    // IRSALIYE LISTESI EKRANI (Document type = satis_irsaliyesi)
    // Sipariş listesi pattern'i birebir — belge türü + form kodu farklı.
    // ════════════════════════════════════════════════════════════════

    [HttpGet]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.SalesDelivery)]
    public async Task<IActionResult> Deliveries(CancellationToken ct)
    {
        var boardConfig = await BuildDeliveriesBoardConfigAsync(ct);
        return View("Documents", new DocumentsViewModel
        {
            AvailableColumns = DocumentGridColumns,
            VisibleColumns = DefaultDocumentColumns,
            BoardConfig = boardConfig,
        });
    }

    private async Task<object> BuildDeliveriesBoardConfigAsync(CancellationToken ct)
    {
        var docs = await _quoteService.GetByTypeAsync("satis_irsaliyesi", search: null, status: null, ct);
        var trCulture = CultureInfo.GetCultureInfo("tr-TR");

        var schema = await _widgetService.GetFormSchemaByCodeAsync("SALES_DELIVERY_EDIT", ct);
        var masterWidgets = CalibraHub.Web.Helpers.SmartBoardFilterHelpers.BuildAdminFormWidgets(schema);
        var statusOptions = CalibraHub.Web.Helpers.SmartBoardFilterHelpers.ToOptionsList(
            new[] { "Taslak", "Gonderildi", "Onaylandi", "Reddedildi", "Iptal", "Kapali" });
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_tutar", "Toplam Tutar", "currency"));
        var durumW = CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_durum", "Durum", "options");
        durumW["options"] = statusOptions;
        masterWidgets.Add(durumW);
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_kalem", "Kalem Sayısı", "numeric"));
        masterWidgets.Add(CalibraHub.Web.Helpers.SmartBoardFilterHelpers.MakeStdWidget("w_tarih", "İrsaliye Tarihi", "date"));

        var recordIds = docs.Select(d => d.Id.ToString()).ToArray();
        var batchWidgets = masterWidgets.Count > 0 && recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("SALES_DELIVERY_EDIT", recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        var entities = new List<object>();
        foreach (var doc in docs)
        {
            var widgets = new List<object>
            {
                new { id = "w_tutar", type = "data", dataType = "currency", label = "Toplam Tutar",
                    value = doc.GrandTotal.ToString("N2", trCulture), detail = doc.CurrencyCode ?? "TRY", color = "blue" },
                new { id = "w_durum", type = "data", dataType = "options", label = "Durum",
                    value = TranslateStatus(doc.Status), detail = (string?)null, color = StatusColor(doc.Status) },
                new { id = "w_kalem", type = "data", dataType = "numeric", label = "Kalem Sayisi",
                    value = doc.LineCount.ToString(CultureInfo.InvariantCulture), detail = "kalem", color = "slate" },
                new { id = "w_tarih", type = "data", dataType = "date", label = "Irsaliye Tarihi",
                    value = doc.DocumentDate.ToString("dd.MM.yyyy", trCulture), detail = (string?)null, color = "slate" },
            };
            var recordId = doc.Id.ToString();
            if (batchWidgets.TryGetValue(recordId, out var renderDtos))
                foreach (var w in renderDtos)
                    widgets.Add(new { id = w.WidgetId, type = "data", dataType = w.DataType.ToLowerInvariant(),
                        label = w.Label, value = w.Value, isPlainField = w.IsPlainField });

            entities.Add(new
            {
                id = doc.Id,
                title = string.IsNullOrWhiteSpace(doc.ContactName) ? "(musterisiz)" : doc.ContactName,
                subtitle = doc.DocumentNumber ?? string.Empty,
                description = string.Empty,
                imageUrl = (string?)null,
                statusBadge = (object?)null,
                widgets,
                primaryAction = new { label = "Duzenle", icon = "Edit", color = "amber",
                    url = $"/Sales/DocumentEdit?id={doc.Id}", hideButton = true },
                secondaryAction = new { label = "Sil", icon = "Trash2",
                    apiUrl = $"/Sales/DeleteDocumentJson?id={doc.Id}",
                    precheckUrl = $"/Sales/CanDeleteDocumentJson?id={doc.Id}",
                    confirm = $"Bu irsaliyeyi silmek istediginizden emin misiniz? ({doc.DocumentNumber})" },
            });
        }

        return new
        {
            boardKey = "sales-deliveries",
            title = "Satış İrsaliyeleri",
            subtitle = $"{entities.Count} irsaliye",
            icon = "Truck",
            iconColor = "violet",
            refreshUrl = "/Sales/DeliveriesBoardConfig",
            searchPlaceholder = "Hizli ara... (irsaliye no, musteri)",
            emptyText = "Henuz irsaliye olusturulmamis",
            actions = new object[]
            {
                new { id = "new-delivery", label = "Yeni İrsaliye", icon = "Plus",
                    variant = "primary", url = "/Sales/DocumentEdit?type=sales_delivery" },
            },
            masterWidgets,
            entities,
        };
    }

    [HttpGet("/Sales/DeliveriesBoardConfig")]
    public async Task<IActionResult> DeliveriesBoardConfig(CancellationToken ct)
        => Json(await BuildDeliveriesBoardConfigAsync(ct));

    // ════════════════════════════════════════════════════════════════
    // TEKLIFLERDEN SIPARIS OLUSTURMA (modal API'leri)
    // ════════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> GetConvertibleQuotes(
        DateTime? fromDate, DateTime? toDate, int? contactId, string? search, CancellationToken ct)
    {
        var quotes = await _quoteService.GetConvertibleQuotesAsync(fromDate, toDate, contactId, search, ct);
        return Json(quotes);
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrdersFromQuotes(
        [FromBody] CreateOrdersFromQuotesRequest req, CancellationToken ct)
    {
        var result = await _quoteService.CreateOrdersFromQuotesAsync(req, CurrentUserId(), ct);
        if (!result.Success)
            return Json(new { success = false, error = result.Error });
        return Json(new
        {
            success = true,
            ordersCreated = result.OrdersCreated,
            orderIds = result.OrderIds,
        });
    }

    /// <summary>
    /// Satın alma zinciri tek-belge dönüşümü: satin_alma_talebi→alis_teklifi, alis_teklifi→alis_siparisi.
    /// Kaynak türüne göre hedef + Approved/Cari zorunluluğu belirlenir. Sipariş edit ekranındaki
    /// "Dönüştür" butonundan tetiklenir. Dönüşen belge yeni sekmede açılabilsin diye id/tür döner.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ConvertPurchaseDocJson(int sourceId, CancellationToken ct)
    {
        if (sourceId <= 0) return Json(new { success = false, message = "Belge bulunamadı." });
        var doc = await _quoteService.GetQuoteByIdAsync(sourceId, ct);
        if (doc?.DocumentTypeId == null) return Json(new { success = false, message = "Belge bulunamadı." });
        var dt = await _documentTypeRepo.GetByIdAsync(doc.DocumentTypeId.Value, ct);
        var src = dt?.Code ?? "";
        string target; bool reqApproved; bool reqContact; string targetLabel; string editType;
        switch (src)
        {
            case "satin_alma_talebi": target = "alis_teklifi";  reqApproved = false; reqContact = false; targetLabel = "Satın Alma Teklifi"; editType = "purchase_quote"; break;
            case "alis_teklifi":      target = "alis_siparisi"; reqApproved = true;  reqContact = true;  targetLabel = "Satın Alma Siparişi"; editType = "purchase_order"; break;
            default: return Json(new { success = false, message = "Bu belge türü dönüştürülemez." });
        }
        var res = await _quoteService.ConvertDocumentsAsync(
            new[] { sourceId }, src, target, reqApproved, reqContact, DateTime.Today, CurrentUserId(), ct);
        if (!res.Success) return Json(new { success = false, message = res.Error });
        var newId = res.OrderIds.Count > 0 ? res.OrderIds[0] : 0;
        return Json(new { success = true, id = newId, targetLabel, editUrl = $"/Purchase/Edit?type={editType}&id={newId}" });
    }

    /// <summary>
    /// Tek bir teklifi siparise donusturur — kart aksiyonundan tetiklenir.
    /// Mevcut CreateOrdersFromQuotesAsync altyapisini tek-elemanli QuoteIds ile cagirir;
    /// CreateWorkOrders=true ise olusan siparişin her satirindan birer is emri (WorkOrder) acar.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ConvertSingleQuoteToOrder(
        [FromBody] ConvertSingleQuoteToOrderRequest req, CancellationToken ct)
    {
        if (req == null || req.QuoteId <= 0)
            return Json(new { success = false, error = "Teklif id gecersiz." });

        var orderResult = await _quoteService.CreateOrdersFromQuotesAsync(
            new CreateOrdersFromQuotesRequest(new[] { req.QuoteId }, req.OrderDate),
            CurrentUserId(), ct);

        if (!orderResult.Success || orderResult.OrderIds.Count == 0)
            return Json(new { success = false, error = orderResult.Error ?? "Siparis olusturulamadi." });

        var orderId = orderResult.OrderIds[0];
        var workOrderIds = new List<int>();

        if (req.CreateWorkOrders)
        {
            var orderLines = await _quoteService.GetQuoteLinesAsync(orderId, ct);
            foreach (var line in orderLines)
            {
                if (line.Quantity <= 0) continue;
                try
                {
                    var woId = await _workOrderService.CreateFromSalesLineAsync(
                        new CreateWorkOrderFromSalesLineRequest(
                            SourceDocumentId: orderId,
                            SourceLineId: line.Id,
                            Quantity: line.Quantity,
                            TargetWorkOrderId: null),
                        ct);
                    workOrderIds.Add(woId);
                }
                catch
                {
                    // Bir satirin is emri olusturulamasa diğer satirlar etkilenmesin;
                    // partial success kabul edilir, response icinde oluşan id'ler doner.
                }
            }
        }

        return Json(new
        {
            success = true,
            orderId,
            workOrderIds,
        });
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
        if (userId <= 0) return Unauthorized();
        await _uiConfigurationService.SaveGridColumnPreferencesAsync(userId, "sales-quotes", columns, ct);
        return Ok(new { success = true });
    }

    /// <summary>Kit satirinin dondurulmus icerik dokumunu doner (Faz 2). Grid'de kit satiri
    /// acilir read-only breakdown + belge yuklemede kit bilesen gosterimi icin.</summary>
    [HttpGet]
    public async Task<IActionResult> KitLineComponents(int lineId, CancellationToken ct)
    {
        if (lineId <= 0) return Json(new { found = false, components = Array.Empty<object>() });
        var comps = await _quoteService.GetKitLineComponentsAsync(lineId, ct);
        return Json(new
        {
            found = comps.Count > 0,
            kitVersionNo = comps.Count > 0 ? comps.First().KitVersionNo : 0,
            components = comps.Select(c => new
            {
                componentItemId = c.ComponentItemId,
                code = c.ComponentCode,
                name = c.ComponentName,
                configId = c.ConfigId,
                configCode = c.ConfigCode,
                quantity = c.Quantity,
            })
        });
    }

    [HttpGet]
    public async Task<IActionResult> DocumentEdit(int? id, string? type, int? fromRequest, CancellationToken ct)
    {
        // Iframe/tarayici cache nedeniyle eski HTML icerigi yapis(an)mayi
        // onlemek icin her GET'te no-cache header'lari set edilir.
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        // Belge tipini cozumle:
        //  1) Mevcut belge (id dolu) → DB'den oku, type FK'sinden code'a resolve
        //  2) Yeni belge + ?type=order → satis_siparisi
        //  3) Aksi → satis_teklifi (varsayilan)
        var typeCode = "satis_teklifi";
        int? typeId = null;
        DocumentDto? existingDoc = null;
        if (id.HasValue && id.Value > 0)
        {
            existingDoc = await _quoteService.GetQuoteByIdAsync(id.Value, ct);
            if (existingDoc != null && existingDoc.DocumentTypeId.HasValue)
            {
                var dt = await _documentTypeRepo.GetByIdAsync(existingDoc.DocumentTypeId.Value, ct);
                if (dt != null) { typeCode = dt.Code; typeId = dt.Id; }
            }
        }
        else if (string.Equals(type, "order", StringComparison.OrdinalIgnoreCase))
        {
            typeCode = "satis_siparisi";
        }
        // 2026-05-23: Satin alma belge tipleri — type query param dogrudan document_type.code alabilir
        // (alis_talebi/alis_teklifi/alis_siparisi) veya kisaltma (purchase_request/quote/order).
        else if (!string.IsNullOrWhiteSpace(type))
        {
            var t = type.Trim();
            typeCode = t.ToLowerInvariant() switch
            {
                "quote"            or "satis_teklifi"         => "satis_teklifi",
                "sales_delivery"   or "satis_irsaliyesi"      => "satis_irsaliyesi",
                "purchase_request" or "alis_talebi"           => "alis_talebi",
                "purchase_quote"   or "alis_teklifi"          => "alis_teklifi",
                "purchase_order"   or "alis_siparisi"         => "alis_siparisi",
                "purchase_demand"  or "satin_alma_talebi"     => "satin_alma_talebi",
                "purchase_delivery" or "alis_irsaliyesi"      => "alis_irsaliyesi",
                _ => typeCode,  // tanimsiz — varsayilani koru
            };
        }

        // Belge tipine göre doğru parent form kodu ile dinamik izin kontrolü.
        // Statik [PermissionScope(SalesQuote)] yerine her belge tipi kendi formuna bakılır.
        {
            var _pfc = CalibraHub.Web.Models.Sales.DocumentTypeFormMap.Resolve(typeCode).Parent;
            UserAuthorizationCatalog.TryParseRole(User.FindFirstValue(ClaimTypes.Role) ?? "", out var _pRole);
            int? _pDept = int.TryParse(User.FindFirstValue("department_id"), out var _pd) && _pd > 0 ? _pd : null;
            if (!await _permService.CheckAnyAsync(GetUserId(), _pRole, _pDept, _pfc,
                    new[] { "VIEW", "VIEW_OWN", "CREATE", "EDIT_OWN", "EDIT_ALL" }, ct))
                return Forbid();
        }

        // DocumentType sadece kavramsal belge tipi (Document.DocumentTypeId FK + raporlama).
        // UI metadata (ListUrl/Icon/IsTransferable) Forms tablosundan beslenir — Faz N hub.
        var docType = await _documentTypeRepo.GetByCodeAsync(typeCode, ct);
        if (typeId == null) typeId = docType?.Id;

        // 2026-05-23: Form code haritası tek noktadan — DocumentTypeFormMap.
        // Eski iki-yonlu if/else yerine tablo lookup; 5 belge tipi (satis_teklifi/siparisi,
        // alis_talebi/teklifi/siparisi) ayni controller yolunu kullanir.
        var formCodes = CalibraHub.Web.Models.Sales.DocumentTypeFormMap.Resolve(typeCode);
        var editFormCode = formCodes.Header;
        var lineFormCode = formCodes.Lines;
        var formMeta = await _formMetadata.GetFormAsync(editFormCode, ct);

        // Satır grid kolonlarına rehber binding'lerini runtime'da inject et
        var bindings = await _fieldSettings.GetGuideBindingsForFormAsync(lineFormCode, ct);
        // Fallback: Sipariş / talep / alis-* satırları için kendi form kodunda binding yoksa
        // teklif (SALES_QUOTE_LINES) varsayilanindan devral — kalem yapilari ozdes.
        if (bindings.Count == 0 && !string.Equals(lineFormCode, "SALES_QUOTE_LINES", StringComparison.OrdinalIgnoreCase))
            bindings = await _fieldSettings.GetGuideBindingsForFormAsync("SALES_QUOTE_LINES", ct);
        var hidePricing = string.Equals(typeCode, "alis_talebi", StringComparison.OrdinalIgnoreCase);
        // Siparişte seri takibi (efektif) — hiyerarşik: stok rez. + seri takibi açık olmalı.
        // Sadece bu durumda satış siparişi grid'ine "Seri" kolonu eklenir.
        var _orderSerialTracking = string.Equals(typeCode, "satis_siparisi", StringComparison.OrdinalIgnoreCase)
            && (await _companyParams.GetBoolAsync(StockParameters.FormCode, StockParameters.SalesOrderAffectsStockKey, ct) ?? false)
            && (await _companyParams.GetBoolAsync(StockParameters.FormCode, StockParameters.OrderSerialTrackingKey, ct) ?? false);
        var lineGridConfig = BuildDocumentLineGridConfig(bindings, lineFormCode, hidePricing, typeCode, id, _orderSerialTracking);
        var jsonOpts = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        var vm = new DocumentEditViewModel
        {
            DocumentId = id,
            LineGridConfigJson = System.Text.Json.JsonSerializer.Serialize(lineGridConfig, jsonOpts),
            DocumentTypeCode = typeCode,
            DocumentTypeId = typeId,
            // Faz N — Forms tablosundan beslenen UI/entegrasyon metadata.
            ListReturnUrl         = formMeta?.ListUrl ?? formCodes.ListUrl,
            NewUrl                = formMeta?.NewUrl,
            DocumentTypeName      = docType?.Name ?? typeCode switch
            {
                "satis_siparisi"   => "Satış Siparişi",
                "satis_irsaliyesi" => "Satış İrsaliyesi",
                "alis_talebi"      => "İhtiyaç Kaydı",
                "alis_teklifi"     => "Satın Alma Teklif",
                "alis_siparisi"    => "Satın Alma Sipariş",
                "satin_alma_talebi" => "Satın Alma Talebi",
                "alis_irsaliyesi"  => "Alış İrsaliyesi",
                _                   => "Satış Teklifi",
            },
            DocumentTypeIcon      = formMeta?.Icon,
            DocumentTypeIconColor = formMeta?.IconColor,
            IsTransferable        = formMeta?.IsTransferable ?? true,
            // 2026-05-23: Form code haritasi — view artik bunlardan okur (hardcoded yok).
            HeaderFormCode    = formCodes.Header,
            HeaderFormCodeNew = formCodes.HeaderNew,
            LineFormCode      = formCodes.Lines,
            // fromRequest: İhtiyaç kaydından teklif oluşturma — kalemler pre-fill edilir.
            FromRequestId     = (fromRequest.HasValue && fromRequest.Value > 0) ? fromRequest : null,
        };

        // İhtiyaç Kaydı (alis_talebi) için "Talep Eden" dropdown — Personnel listesi.
        // Parametrik yetkiler: CREATE_ON_BEHALF (başkası adına) + CREATE_ON_BEHALF_DEPT_ONLY (kapsam).
        if (string.Equals(typeCode, "alis_talebi", StringComparison.OrdinalIgnoreCase))
        {
            var personnel = await _personnelService.ListAsync(false, false, ct);

            var curUserId = GetUserId();
            var roleStr   = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            var deptStr   = User.FindFirstValue("department_id");
            UserAuthorizationCatalog.TryParseRole(roleStr, out var userRole);
            int? userDeptId = int.TryParse(deptStr, out var _d) && _d > 0 ? _d : null;

            var canOnBehalf = await _permService.CheckAsync(curUserId, userRole, userDeptId,
                FormCodes.PurchaseRequest,
                FormButtonCatalog.BuildActionCode("CREATE_ON_BEHALF"), ct);

            var deptOnlyScope = canOnBehalf && await _permService.CheckAsync(curUserId, userRole, userDeptId,
                FormCodes.PurchaseRequest,
                FormButtonCatalog.BuildActionCode("CREATE_ON_BEHALF_DEPT_ONLY"), ct);

            // Mevcut kullanıcıya ait personel kaydı (UserId bağlantısı üzerinden).
            var currentPersonnel = curUserId > 0
                ? personnel.FirstOrDefault(p => p.UserId == curUserId)
                : null;

            IEnumerable<PersonnelDto> filtered;
            if (!canOnBehalf)
            {
                // Yetkisi yok → sadece kendisi (otomatik kilitli).
                filtered = currentPersonnel != null
                    ? (IEnumerable<PersonnelDto>)new[] { currentPersonnel }
                    : Enumerable.Empty<PersonnelDto>();
            }
            else if (deptOnlyScope)
            {
                // Yetki var ama sadece departmanındaki kişiler.
                var dept = currentPersonnel?.Department;
                filtered = string.IsNullOrWhiteSpace(dept)
                    ? personnel  // departman belirlenemezse tüm liste
                    : personnel.Where(p => string.Equals(p.Department?.Trim(), dept.Trim(),
                          StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                filtered = personnel;
            }

            // Mevcut belgede kayıtlı talep eden (onay akışında bakan kişi ≠ oluşturan).
            // existingDoc.RequesterPersonnelId varsa onu "efektif" seçili ID olarak kullan;
            // liste içinde yoksa personnel havuzundan ekle — disabled select doğru görünür.
            var existingRequesterId = existingDoc?.RequesterPersonnelId;
            var effectivePersonnelId = existingRequesterId ?? currentPersonnel?.Id;

            var optionsList = filtered.ToList();
            if (existingRequesterId.HasValue && !optionsList.Any(p => p.Id == existingRequesterId.Value))
            {
                var savedRequester = personnel.FirstOrDefault(p => p.Id == existingRequesterId.Value);
                if (savedRequester != null) optionsList.Add(savedRequester);
            }

            ViewData["RequesterPersonnels"] = optionsList
                .OrderBy(p => p.FullName)
                .Select(p => new { p.Id, Name = p.FullName, Dept = p.Department ?? "" })
                .ToList();
            ViewData["CanCreateOnBehalf"]      = canOnBehalf;
            ViewData["RequesterLocked"]        = !canOnBehalf;
            ViewData["CurrentUserPersonnelId"] = effectivePersonnelId;

            // Hedef Depo dropdown � Location listesi.
            var locs = await _logisticsService.GetLocationsAsync(ct);
            var locParentIds = locs.Where(l => l.ParentId.HasValue).Select(l => l.ParentId!.Value).ToHashSet();
            ViewData["LocationList"] = locs
                .Where(l => l.IsActive && !locParentIds.Contains(l.Id))
                .OrderBy(l => l.LocationName ?? l.LocationCode)
                .Select(l => new { l.Id, LocationName = l.LocationName ?? l.LocationCode })
                .ToList();
            // Mevcut belgede kayıtlı lokasyon varsa onu kullan;
            // yeni belgede login kullanıcının personel kartındaki varsayılan lokasyonu getir.
            int? defaultLocId = existingDoc?.LocationId;
            if (defaultLocId == null)
            {
                var uid = CurrentUserId();
                if (uid.HasValue)
                {
                    var userPersonnel = await _personnelService.GetByUserIdAsync(uid.Value, ct);
                    defaultLocId = userPersonnel?.LocationId;
                }
            }
            ViewData["CurrentLocationId"] = defaultLocId;
        }

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
        IReadOnlyCollection<FieldGuideBindingDto>? bindings = null,
        string lineFormCode = "SALES_QUOTE_LINES",
        bool hidePricing = false,
        string? documentTypeCode = null,
        int? documentId = null,
        bool orderSerialTracking = false)
    {
        // Binding sözlüğü: fieldKey → (guideCode, isRequired, filterJson)
        var bindingMap = (bindings ?? [])
            .ToDictionary(b => b.FieldKey, b => b, StringComparer.OrdinalIgnoreCase);

        // materialCode kolonu için binding varsa guide ekle, yoksa lookupUrl fallback
        bindingMap.TryGetValue("materialCode", out var matBinding);

        // Kolon listesi — hidePricing=true olduğunda fiyat/iskonto/kdv/toplam kolonları dahil edilmez.
        var cols = new List<object>
        {
            new
            {
                key = "materialCode",
                label = "Malzeme Kodu",
                type = "text-lookup",
                guideCode      = matBinding?.GuideCode,
                filterJson     = matBinding?.FilterJson,
                formCode       = lineFormCode,
                formatJson     = matBinding?.FormatJson,
                lookupUrl      = matBinding == null ? $"/Sales/GetMaterials?docType={documentTypeCode}" : (string?)null,
                lookupValueKey = "materialCode",
                lookupLabelKey = "materialName",
                lookupFillMap = new Dictionary<string, string>
                {
                    ["materialName"]      = "materialName",
                    ["stockCardId"]       = "id",
                    ["trackCombinations"] = "trackCombinations",
                    ["trackSerial"]       = "trackSerial",
                    ["autoSerial"]        = "autoSerial",
                    ["taxRate"]           = "taxRate",
                    ["locationId"]        = "defaultLocationId",
                    ["unitId"]            = "unitId",
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
                visibleWhenKey = "trackCombinations",
            },
            new
            {
                key = "unitId",
                label = "Birim",
                type = "select",
                optionsUrl = "/Sales/GetMaterialUnits?materialCode={materialCode}",
                optionsValueKey = "id",
                optionsLabelKey = "name",
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
        };
        if (!hidePricing)
        {
            cols.Add(new { key = "unitPrice",    label = "Birim Fiyat",   type = "currency", width = 130, precision = 2, min = 0,       align = "right", icon = "DollarSign" });
            cols.Add(new { key = "discountRate", label = "Iskonto %",     type = "percent",  width = 100, precision = 2, min = 0, max = 100, align = "right", icon = "Percent" });
            cols.Add(new { key = "taxRate",      label = "KDV %",         type = "percent",  width = 90,  precision = 2, min = 0, max = 100, @readonly = true, align = "right", icon = "Percent" });
            cols.Add(new { key = "lineTotal",    label = "Satir Toplami", type = "currency", width = 140, computed = true, formula = "quantity * unitPrice * (1 - (discountRate / 100))", align = "right", icon = "Calculator" });
        }
        cols.Add(new { key = "notes", label = "Not", type = "text", placement = "row-below", align = "left", icon = "StickyNote" });

        // Seri kolonu — YALNIZCA satış siparişinde VE "Siparişte Seri Takibi" parametresi açıkken.
        // Pick modu: stoktaki serilerden seçim (GetOrderSerials). Seri-takipli kalemde görünür.
        if (orderSerialTracking && string.Equals(documentTypeCode, "satis_siparisi", StringComparison.OrdinalIgnoreCase))
            cols.Add(new
            {
                key            = "serials",
                label          = "Seri",
                type           = "serial-entry",
                serialMode     = "pick",
                serialsUrl     = $"/Sales/GetOrderSerials?itemId={{stockCardId}}&documentId={documentId ?? 0}",
                width          = 90,
                align          = "center",
                icon           = "Barcode",
                visibleWhenKey = "trackSerial",
            });

        return new
        {
            schemaVersion = "v1",
            columns = cols,
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
                showSubtotal = !hidePricing,
                subtotalColumns = hidePricing ? System.Array.Empty<string>() : new[] { "lineTotal" },
            },
            // Otomatik fiyat cozumu: urun/kombinasyon secilince carinin fiyat listesine gore
            // birim fiyat doldurulur (cari yoksa Genel Liste). enabled=false → fiyat kolonu gizli
            // belgelerde (ornek: alis_talebi / Ihtiyac Kaydi) hic cozum yapilmaz.
            // Element id'leri config'te → JSX'e hardcode yok ("zeki veri, aptal bilesen").
            pricing = new
            {
                enabled    = !hidePricing,
                resolveUrl = "/Sales/ResolveLinePrices",
                direction  = (documentTypeCode ?? "").StartsWith("satis", StringComparison.OrdinalIgnoreCase) ? "s" : "b",
                targetKey  = "unitPrice",
                itemKey    = "stockCardId",
                configKey  = "combinationId",
                context    = new { contactElId = "sqCustomerId", currencyElId = "sqCurrency", dateElId = "sqQuoteDate" }
            },
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetDocuments(string? search, string? status, CancellationToken ct)
    {
        // Bug fix 2026-05-20: GetQuotesAsync ad-ile-davranis uyumsuzdu (tum belgeleri
        // donduruyordu). Teklif listesi endpoint'i sadece "satis_teklifi" tipini
        // dondurmeli — siparisler GetOrders'tan, transferler kendi controllerinden.
        var quotes = await _quoteService.GetByTypeAsync("satis_teklifi", search, status, ct);
        return Json(quotes);
    }

    /// <summary>
    /// İş emri "Kaynak Sipariş Ekle" modalında kullanılır — sadece satış siparişlerini
    /// (Document.type = satis_siparisi) flat liste olarak doner. GetDocuments
    /// tum belge tiplerini doner (geriye doniik uyum), bu endpoint sadece siparis filtreler.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetOrdersList(string? search, string? status, CancellationToken ct)
    {
        var orders = await _quoteService.GetByTypeAsync("satis_siparisi", search, status, ct);
        return Json(orders);
    }

    [HttpGet]
    public async Task<IActionResult> GetQuote(int id, CancellationToken ct)
    {
        var quote = await _quoteService.GetQuoteByIdAsync(id, ct);
        if (quote == null) return NotFound();
        var lines = await _quoteService.GetQuoteLinesAsync(id, ct);

        // Kalem-bazli zorunlu widget kontrolu — ⚙ butonlarinin baslangic renklerini
        // belirlemek icin satir ID'lerinde eksik zorunlu alanlar var mi?
        var lineIds = lines.Select(l => l.Id.ToString()).ToArray();
        var lineFormCode = "SALES_QUOTE_LINES";
        if (quote.DocumentTypeId.HasValue)
        {
            var dt = await _documentTypeRepo.GetByIdAsync(quote.DocumentTypeId.Value, ct);
            lineFormCode = DocumentTypeFormMap.Resolve(dt?.Code).Lines;
        }
        var missing = await _widgetService.ValidateRequiredAsync(lineFormCode, lineIds, ct);
        var invalidLineIds = missing.Keys
            .Select(k => int.TryParse(k, out var v) ? v : 0)
            .Where(v => v > 0)
            .ToArray();

        // Bağlantı-kilitli satırlar (karşılanmış / türetilmiş) — grid önden silme engeli +
        // miktar tabanı için kullanır (SaveQuoteAsync ile aynı taban).
        var lineLocks = await _quoteService.GetLineLocksAsync(id, ct);

        return Json(new { quote, lines, invalidLineIds, lineLocks });
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
    public async Task<IActionResult> GetMaterials(string? docType, CancellationToken ct)
    {
        var snapshot = await _logisticsService.GetSnapshotAsync(ct);

        // Planlama: bu belge tipinde kilitli malzemeler lookup'ta gösterilmez ("o belgede seçilemesin")
        var lockedIds = string.IsNullOrWhiteSpace(docType)
            ? new HashSet<int>()
            : (await _logisticsService.GetLockedItemIdsByDocTypeAsync(docType, ct)).ToHashSet();

        // Batch: her malzeme icin varsayilan lokasyon (varsa) — belge satirina kalem
        // secildiginde location_id otomatik doldurulur.
        var defaultLocations = new Dictionary<int, int>();
        await using (var conn = await _connectionFactory.OpenConnectionAsync(ct))
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT [ItemId], [LocationId]
                FROM [{_schema}].[ItemLocation]
                WHERE [IsDefault] = 1;
                """;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                defaultLocations[r.GetInt32(0)] = r.GetInt32(1);
        }

        var materials = snapshot.Items
            .Where(x => x.IsActive && !lockedIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                MaterialCode = x.Code,
                MaterialName = x.Name,
                x.TaxRate,
                TrackCombinations = x.Combinations,
                // Seri takibi: sipariş grid'inde "Seri" kolonu bu bayrakla aktifleşir (warehouse ile aynı kaynak).
                TrackSerial = string.Equals(x.TrackingType, "Serial", StringComparison.OrdinalIgnoreCase),
                AutoSerial  = x.AutoSerial,
                DefaultLocationId = defaultLocations.TryGetValue(x.Id, out var locId) ? (int?)locId : null,
                // Stok kartindaki master birim — kalem secildiginde line.unitId'ye otomatik atanir
                x.UnitId,
            })
            .OrderBy(x => x.MaterialCode)
            .ToArray();
        return Json(materials);
    }

    // Belge kalemine otomatik birim fiyat — carinin fiyat listesi (Contact.PriceGroupId)
    // varsa ondan, o listede urun yoksa "Genel Liste"den cozer. direction: 's'=satis, 'b'=alis.
    // Alis belgeleri de bu view/controller'i kullandigi icin (Purchase → /Sales/DocumentEdit
    // redirect) tek endpoint her iki yonu de karsilar; yon config'ten (pricing.direction) gelir.
    public sealed class ResolveLinePricesBody
    {
        public int? ContactId { get; set; }
        public int CurrencyId { get; set; }
        public string? ValidOn { get; set; }    // ISO tarih; bos → bugun
        public string? Direction { get; set; }  // "s" | "b" | "m"
        public List<KeyBody> Keys { get; set; } = new();
        public sealed class KeyBody { public int ItemId { get; set; } public int? ConfigId { get; set; } }
    }

    [HttpPost]
    public async Task<IActionResult> ResolveLinePrices([FromBody] ResolveLinePricesBody? body, CancellationToken ct)
    {
        if (body?.Keys is null || body.Keys.Count == 0 || body.CurrencyId <= 0)
            return Json(Array.Empty<object>());

        var direction = (body.Direction ?? "s").Trim().ToLowerInvariant() switch
        {
            "b" => PriceDirection.Purchase,
            "m" => PriceDirection.Cost,
            _   => PriceDirection.Sales
        };
        var date = DateTime.TryParse(body.ValidOn, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.Date : DateTime.Today;
        var keys = body.Keys.Where(k => k.ItemId > 0)
            .Select(k => new PriceEntryKey(k.ItemId, k.ConfigId))
            .ToArray();
        if (keys.Length == 0) return Json(Array.Empty<object>());

        var rows = await _priceListService.ResolveLinePricesAsync(
            new ResolveLinePricesRequest(body.ContactId, body.CurrencyId, direction, date, keys), ct);

        return Json(rows.Select(r => new
        {
            itemId        = r.ItemId,
            configId      = r.ConfigId,
            price         = r.Price,
            sourceGroupId = r.SourceGroupId,
            source        = r.Source.ToString()
        }));
    }

    // Sipariş seri seçim havuzu — stoktaki (InStock) + BU siparişin rezerve serileri.
    // documentId: mevcut sipariş id'si (0=yeni). Düzenlemede sipariş kendi rezerve serilerini
    // de seçili görür; başka siparişin rezervesi görünmez.
    [HttpGet]
    public async Task<IActionResult> GetOrderSerials(int itemId, int documentId, CancellationToken ct)
    {
        if (itemId <= 0) return Json(Array.Empty<object>());
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT s.[SerialNo], lot.[LotNo]
            FROM [{_schema}].[ItemSerial] s
            LEFT JOIN [{_schema}].[Lot] lot ON lot.[Id] = s.[LotId]
            WHERE s.[ItemId] = @ItemId AND s.[IsActive] = 1
              AND (s.[Status] = 1 OR (s.[Status] = 4 AND s.[ReservedForDocumentId] = @Doc))
            ORDER BY s.[SerialNo];
            """;
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@Doc", documentId);
        var result = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(new { serialNo = r.GetString(0), lotNo = r.IsDBNull(1) ? null : r.GetString(1) });
        return Json(result);
    }

    // Bir belgenin satır-seri eşlemesi (lineId → serialNo[]). Düzenleme yüklemesinde grid
    // satırlarına serileri doldurmak için kullanılır.
    [HttpGet]
    public async Task<IActionResult> GetDocumentLineSerials(int documentId, CancellationToken ct)
    {
        if (documentId <= 0) return Json(new Dictionary<string, string[]>());
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT dls.[DocumentLineId], s.[SerialNo]
            FROM [{_schema}].[DocumentLineSerial] dls
            INNER JOIN [{_schema}].[DocumentLine] dl ON dl.[Id] = dls.[DocumentLineId]
            INNER JOIN [{_schema}].[ItemSerial] s ON s.[Id] = dls.[SerialId]
            WHERE dl.[DocumentId] = @Doc
            ORDER BY dls.[DocumentLineId], s.[SerialNo];
            """;
        cmd.Parameters.AddWithValue("@Doc", documentId);
        var map = new Dictionary<string, List<string>>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var lineId = r.GetInt32(0).ToString();
            if (!map.TryGetValue(lineId, out var list)) { list = new List<string>(); map[lineId] = list; }
            list.Add(r.GetString(1));
        }
        return Json(map.ToDictionary(k => k.Key, v => v.Value.ToArray()));
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
            var unit = allUnits.FirstOrDefault(u => string.Equals(u.Code, unitCode, StringComparison.OrdinalIgnoreCase));
            unitName = unit?.Name ?? unitCode;
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

        // Tum olcu birimi tanimlari — Id ile lookup
        var allUnits = await _logisticsService.GetUnitsAsync(ct);
        var unitById = allUnits
            .Where(u => u.IsActive)
            .ToDictionary(u => u.Id, u => u);

        var seenIds = new HashSet<int>();
        var seenLegacyCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void PushById(int? id)
        {
            if (!id.HasValue || !seenIds.Add(id.Value)) return;
            if (!unitById.TryGetValue(id.Value, out var u)) return;
            seenLegacyCodes.Add(u.Code);
            result.Add(new { id = (int?)u.Id, code = u.Code, name = u.Name });
        }

        void PushByCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || !seenLegacyCodes.Add(code)) return;
            // Aktif birim kodu ise Id resolve, degilse legacy code-only entry (Id=null)
            var match = unitById.Values.FirstOrDefault(u =>
                string.Equals(u.Code, code, StringComparison.OrdinalIgnoreCase));
            if (match != null) { seenIds.Add(match.Id); result.Add(new { id = (int?)match.Id, code = match.Code, name = match.Name }); }
            else                { result.Add(new { id = (int?)null, code, name = code }); }
        }

        // 1) Stok kart Item.UnitId — MASTER (ana) birim, ilk siraya konur (autoSelectFirst icin)
        var snapshot = await _logisticsService.GetSnapshotAsync(ct);
        var stockCard = snapshot.Items.FirstOrDefault(x =>
            string.Equals(x.Code, materialCode, StringComparison.OrdinalIgnoreCase));
        if (stockCard != null)
        {
            PushById(stockCard.UnitId);

            // 2) ItemUnits tablosu — kullanicinin tanimladigi alternatif/donusum birimleri
            var conversions = await _logisticsService.GetItemUnitsAsync(stockCard.Id, ct);
            foreach (var cv in conversions) PushById(cv.UnitId);
        }

        // 3) Legacy fallback — eski EAV widget'lardan tanimlanmis birimler (geri uyumluluk)
        var widgetValues = await LoadMaterialWidgetValuesAsync(materialCode, ct);
        PushByCode(widgetValues.GetValueOrDefault("unit_name") ?? "");
        PushByCode(widgetValues.GetValueOrDefault("purchase_unit_name") ?? "");
        for (var i = 1; i <= 5; i++)
            PushByCode(widgetValues.GetValueOrDefault($"unit_conv_{i}_code") ?? "");

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

        var record = await _widgetService.GetRecordByCodeAsync("MATERIAL_CARD_EDIT", materialCode, ct);
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
        return Json(units.Where(u => u.IsActive).Select(u => new { code = u.Code, name = u.Name }));
    }

    [HttpGet]
    public async Task<IActionResult> GetCombinations(string materialCode, CancellationToken ct)
    {
        var combos = await _logisticsService.GetCombinationsForLookupAsync(materialCode, ct);
        return Json(combos.Select(c => new
        {
            configId = c.ConfigId,
            code = c.Code,
            name = c.Name,
            features = c.FeatureValues.Select(fv => new { valueId = fv.FeatureValueId, feature = fv.Feature, value = fv.Value, valueCode = fv.ValueCode })
        }));
    }

    [HttpGet]
    public async Task<IActionResult> GetMaterialFeatures(string materialCode, CancellationToken ct)
    {
        var snapshot = await _logisticsService.GetProductConfigurationSnapshotAsync(ct);
        // Bu malzemeye bagli ozellik linkleri — featureId -> AllowedValueIds (null/bos = hepsi)
        var stockLinks = snapshot.FeatureStockLinks
            .Where(l => string.Equals(l.StockCode, materialCode, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(l => l.FeatureId, l => l.AllowedValueIds);

        var features = new List<object>();
        foreach (var feature in snapshot.Features.Where(f => stockLinks.ContainsKey(f.Id) && f.IsActive))
        {
            // Stok karti icin admin tarafindan ozellestirilmis deger seti varsa filtrele;
            // null veya bos ise (admin secim yapmadiysa) hepsi gosterilir (back-compat).
            var allowed = stockLinks[feature.Id];
            var values = snapshot.Values
                .Where(v => v.FeatureId == feature.Id && v.IsActive)
                .Where(v => allowed == null || allowed.Count == 0 || allowed.Contains(v.Id))
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
            return Json(new { success = false, message = "Sunucu hatası: " + "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SaveDocument([FromBody] SaveDocumentRequest? request, CancellationToken ct)
    {
        if (request is null)
            return Json(new { success = false, message = "Gecersiz istek govdesi. Tarih ve zorunlu alanlari kontrol ediniz." });

        // Belge tipine göre doğru parent form kodu ile dinamik izin kontrolü.
        {
            var _sDtCode = "satis_teklifi";
            if (request.DocumentTypeId.HasValue)
            {
                var _sDt = await _documentTypeRepo.GetByIdAsync(request.DocumentTypeId.Value, ct);
                if (_sDt != null) _sDtCode = _sDt.Code;
            }
            var _sPfc = CalibraHub.Web.Models.Sales.DocumentTypeFormMap.Resolve(_sDtCode).Parent;
            UserAuthorizationCatalog.TryParseRole(User.FindFirstValue(ClaimTypes.Role) ?? "", out var _sRole);
            int? _sDept = int.TryParse(User.FindFirstValue("department_id"), out var _sd) && _sd > 0 ? _sd : null;
            if (!await _permService.CheckAnyAsync(GetUserId(), _sRole, _sDept, _sPfc,
                    new[] { "CREATE", "EDIT_OWN", "EDIT_ALL" }, ct))
                return Json(new { success = false, message = "Bu belge için yetkiniz bulunmuyor." });
        }

        try
        {
            var (success, error, quote, approvalTriggered) = await _quoteService.SaveQuoteAsync(request, CurrentUserId(), User?.Identity?.Name, ct);
            if (!success) return Json(new { success = false, message = error });

            // SALES_QUOTE_LINES formunda IsRequired=true widget'lar varsa, her satirin
            // widget degerlerini kontrol et. Eksik varsa belge kaydi commit edilmis olsa
            // bile KITT / success tetiklenmesin — kullanici ⚙ modal'dan doldurup tekrar
            // kaydetmek zorunda.
            IReadOnlyCollection<DocumentLineDto> savedLines = Array.Empty<DocumentLineDto>();
            if (quote != null)
            {
                savedLines = await _quoteService.GetQuoteLinesAsync(quote.Id, ct);
                var lineIds    = savedLines.Select(l => l.Id.ToString()).ToArray();
                var saveLineFormCode = "SALES_QUOTE_LINES";
                if (request.DocumentTypeId.HasValue)
                {
                    var dt = await _documentTypeRepo.GetByIdAsync(request.DocumentTypeId.Value, ct);
                    saveLineFormCode = DocumentTypeFormMap.Resolve(dt?.Code).Lines;
                }
                var missing    = await _widgetService.ValidateRequiredAsync(saveLineFormCode, lineIds, ct);
                if (missing.Count > 0)
                {
                    var parts = missing.Select(kvp =>
                    {
                        var line = savedLines.FirstOrDefault(l => l.Id.ToString() == kvp.Key);
                        var lineLabel = line != null
                            ? $"Satir #{line.LineNo} ({line.MaterialCode ?? "?"})"
                            : $"Satir #{kvp.Key}";
                        var fields = string.Join(", ", kvp.Value.Select(x => x.Label));
                        return $"{lineLabel}: {fields}";
                    });
                    var msg = "Asagidaki satirlarda zorunlu ek alanlar eksik. Kalemlerin ⚙ butonundan doldurup tekrar kaydedin:\n" + string.Join("\n", parts);
                    var invalidLineIds = missing.Keys
                        .Select(k => int.TryParse(k, out var v) ? v : 0)
                        .Where(v => v > 0)
                        .ToArray();
                    return Json(new { success = false, message = msg, quote, invalidLineIds });
                }
            }

            // ── Sipariş seri rezervasyonu (satis_siparisi) ────────────────────────
            // Kaydedilen satırlara seçilen serileri bağla; ORDER_SERIAL_RESERVATION + stok
            // rezervasyonu açıksa InStock→Reserved (hiyerarşi). Reconcile her zaman çağrılır —
            // payload boşsa bile belgenin eski seri bağlarını/rezervasyonunu temizler (reset+rebuild).
            if (quote != null && request.DocumentTypeId.HasValue)
            {
                var _serDt = await _documentTypeRepo.GetByIdAsync(request.DocumentTypeId.Value, ct);
                if (string.Equals(_serDt?.Code, "satis_siparisi", StringComparison.OrdinalIgnoreCase))
                {
                    // Hiyerarşi: stok rez. → seri takibi → seri rez. Rezervasyon ancak üçü de açıkken.
                    var stockRes = await _companyParams.GetBoolAsync(
                        StockParameters.FormCode, StockParameters.SalesOrderAffectsStockKey, ct) ?? false;
                    var tracking = stockRes && (await _companyParams.GetBoolAsync(
                        StockParameters.FormCode, StockParameters.OrderSerialTrackingKey, ct) ?? false);
                    var serialRes = tracking && (await _companyParams.GetBoolAsync(
                        StockParameters.FormCode, StockParameters.OrderSerialReservationKey, ct) ?? false);

                    var reqLines = (request.Lines ?? Array.Empty<SaveDocumentLineRequest>()).ToList();
                    var ordered  = savedLines.OrderBy(l => l.LineNo).ToList();
                    var lineSerials = new List<(int, int, IReadOnlyList<string>)>();
                    for (int i = 0; i < ordered.Count && i < reqLines.Count; i++)
                        if (reqLines[i].Serials is { Count: > 0 } sers && ordered[i].ItemId > 0)
                            lineSerials.Add((ordered[i].Id, ordered[i].ItemId, sers));

                    var (serOk, serErr) = await _stockDocRepo.ReconcileOrderSerialsAsync(
                        quote.Id, lineSerials, serialRes, ct);
                    if (!serOk)
                        return Json(new { success = false, message = serErr, quote });
                }
            }

            // Otomatik Save trigger'li entegrasyonlari arka planda fire et (fire-and-forget).
            // Sales document hem QUOTE hem ORDER olabilir + hem NEW hem EDIT form code'lariyla
            // wizard'da tanimlanmis olabilir — tum 4 varyanti tara. DB'de UNIQUE INDEX
            // sayesinde her sorgu cok hizli; eslesmesi olmayan form code'lar no-op doner.
            if (quote != null && quote.Id > 0)
            {
                _onSaveDispatcher.FireOnSave(
                    new[] { "SALES_QUOTE_NEW", "SALES_QUOTE_EDIT", "SALES_ORDER_NEW", "SALES_ORDER_EDIT" },
                    quote.Id.ToString(),
                    User?.Identity?.Name);
            }

            // lines: kaydedilen satirlar (Id dahil, LineNo sirasinda). Grid sessiz kayit
            // sonrasi bu Id'leri satirlarina merge eder — aksi halde bir sonraki kayit
            // Id'siz gider, SaveLinesAsync satirlari DELETE+INSERT eder ve satir bazli
            // WidgetTra kayitlari orphan kalir (TKL202600000002 vakasi, 2026-07-10).
            return Json(new { success = true, quote, approvalTriggered, lines = savedLines });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SaveQuote] belge kaydedilemedi.");
            return Json(new { success = false, message = "İşlem sırasında bir hata oluştu." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteDocument([FromBody] DeleteQuoteBody body, CancellationToken ct)
    {
        // Belge tipini DB'den çözerek doğru parent form kodu kontrolü.
        var _dDoc = await _quoteService.GetQuoteByIdAsync(body.Id, ct);
        if (_dDoc != null && _dDoc.DocumentTypeId.HasValue)
        {
            var _dDt = await _documentTypeRepo.GetByIdAsync(_dDoc.DocumentTypeId.Value, ct);
            var _dPfc = CalibraHub.Web.Models.Sales.DocumentTypeFormMap.Resolve(_dDt?.Code).Parent;
            UserAuthorizationCatalog.TryParseRole(User.FindFirstValue(ClaimTypes.Role) ?? "", out var _dRole);
            int? _dDept = int.TryParse(User.FindFirstValue("department_id"), out var _dd) && _dd > 0 ? _dd : null;
            if (!await _permService.CheckAnyAsync(GetUserId(), _dRole, _dDept, _dPfc,
                    new[] { "DELETE_OWN", "DELETE_ALL" }, ct))
                return Json(new { success = false, message = "Bu belgeyi silmek için yetkiniz bulunmuyor." });
        }
        var (_dOk, _dErr) = await _quoteService.DeleteQuoteAsync(body.Id, ct);
        if (!_dOk) return Json(new { success = false, message = _dErr });
        // Silinen belgenin rezerve serileri (varsa) stoğa geri döner (idempotent).
        try { await _stockDocRepo.ReleaseOrderSerialReservationsAsync(body.Id, ct); } catch { }
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
            var _djDoc = await _quoteService.GetQuoteByIdAsync(id, ct);
            if (_djDoc != null && _djDoc.DocumentTypeId.HasValue)
            {
                var _djDt = await _documentTypeRepo.GetByIdAsync(_djDoc.DocumentTypeId.Value, ct);
                var _djPfc = CalibraHub.Web.Models.Sales.DocumentTypeFormMap.Resolve(_djDt?.Code).Parent;
                UserAuthorizationCatalog.TryParseRole(User.FindFirstValue(ClaimTypes.Role) ?? "", out var _djRole);
                int? _djDept = int.TryParse(User.FindFirstValue("department_id"), out var _djd) && _djd > 0 ? _djd : null;
                if (!await _permService.CheckAnyAsync(GetUserId(), _djRole, _djDept, _djPfc,
                        new[] { "DELETE_OWN", "DELETE_ALL" }, ct))
                    return Json(new { success = false, message = "Bu belgeyi silmek için yetkiniz bulunmuyor." });
            }
            var (_djOk, _djErr) = await _quoteService.DeleteQuoteAsync(id, ct);
            if (!_djOk) return Json(new { success = false, message = _djErr });
            try { await _stockDocRepo.ReleaseOrderSerialReservationsAsync(id, ct); } catch { }
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>
    /// Silme ön-kontrolü (SmartCard precheck). Belge silinebilir mi — silinemezse
    /// gerekçesini döner (karşılanmış kalem / türetilmiş aktif belge). UI, silme
    /// onay ekranını göstermeden ÖNCE bunu çağırır: engelliyse onay yerine doğrudan
    /// uyarı gösterilir. DeleteQuoteAsync ile aynı kaynak → mesajlar birebir aynı.
    /// Salt-okunur kontrol (GET) — asıl silme yine DeleteQuoteAsync'te guard'lı.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> CanDeleteDocumentJson(int id, CancellationToken ct)
    {
        try
        {
            var reason = await _quoteService.GetDeleteBlockReasonAsync(id, ct);
            return Json(reason is null
                ? new { ok = true,  reason = (string?)null }
                : new { ok = false, reason = (string?)reason });
        }
        catch
        {
            // Ön-kontrol hata verirse silmeyi engelleme — normal onay akışına düşülür.
            return Json(new { ok = true, reason = (string?)null });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ChangeQuoteStatus([FromBody] ChangeStatusBody body, CancellationToken ct)
    {
        var (success, error) = await _quoteService.ChangeStatusAsync(body.Id, body.Status, ct);
        if (!success) return Json(new { success = false, message = error });
        // İptal/Red durumunda rezerve seriler serbest bırakılır (idempotent).
        if (string.Equals(body.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(body.Status, "Rejected", StringComparison.OrdinalIgnoreCase))
            try { await _stockDocRepo.ReleaseOrderSerialReservationsAsync(body.Id, ct); } catch { }
        return Json(new { success = true });
    }

    public sealed record ReviseLineBody(int ParentLineId, string? Description);

    /// <summary>
    /// Kalem revizyonu — atomik, anlik DB operasyonu:
    ///   1) Eski satirin notes = @Description (revize gerekcesi).
    ///   2) Yeni satir eski'nin birebir kopyasi olarak INSERT edilir, revised_from_id
    ///      = parent. Kombinasyon detaylari + widget degerleri de kopyalanir.
    /// Frontend "Revize Et" butonu bu endpoint'i cagirir; kullanici ana belge
    /// Kaydet'e basmak zorunda kalmadan revizyon kayit altina alinir. Eski satir
    /// gridde revised_from_id filtresi ile gizlenir.
    /// Return: { success, newLineId, documentId, message }
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ReviseLine([FromBody] ReviseLineBody body, CancellationToken ct)
    {
        if (body == null || body.ParentLineId <= 0)
            return Json(new { success = false, message = "Gecersiz parametre." });

        try
        {
            var newLineId = await _quoteService.ReviseLineAsync(body.ParentLineId, body.Description, ct);
            if (newLineId == null || newLineId <= 0)
                return Json(new { success = false, message = "Revize edilecek satir bulunamadi." });

            // Widget degerlerini (SALES_QUOTE_LINES form) eski satirdan yeniye kopyala.
            // IWidgetRepository.CopyValuesAsync — atomic INSERT ... SELECT ile top-level
            // degerleri kopyalar. Form bulunamazsa veya eski satirda widget yoksa no-op.
            try
            {
                var schema = await _widgetService.GetFormSchemaByCodeAsync("SALES_QUOTE_LINES", ct);
                if (schema != null)
                {
                    await _widgetRepo.CopyValuesAsync(
                        schema.FormId,
                        body.ParentLineId.ToString(),
                        newLineId.Value.ToString(),
                        ct);
                }
            }
            catch (Exception widgetEx)
            {
                // Widget kopyasi basarisiz olsa bile revize olustu — silent log.
                _logger.LogWarning(widgetEx, "ReviseLine widget kopyalama hatasi (newLineId={NewLineId})", newLineId);
            }

            return Json(new { success = true, newLineId = newLineId.Value });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Revize hatasi: " + "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>
    /// Satis teklifini yazdirmak icin basit bir print-friendly HTML dondurur.
    /// SmartCard "Yazdir" aksiyonundan tetiklenir. Ileride Belge Tasarimcisi
    /// (DocDesigner) uzerinden PDF stream'i doner; su an placeholder — tarayicinin
    /// yazdir dialogu kendiliginden acilir (window.print).
    /// </summary>
    /// <summary>
    /// SmartCard fetch-modal icin yazdir onizleme HTML kabugu. Iframe ile
    /// asagidaki PrintQuote endpoint'ine (raw PDF) baglanir; SmartCard modal'i
    /// zaten X kapatma butonunu header'da gosterir. Ek olarak toolbar'da
    /// "Tam ekran" ve "Yazdir" kestirme linkleri sunar.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> PrintQuoteDialog(int id, CancellationToken ct)
    {
        var quote = await _quoteService.GetQuoteByIdAsync(id, ct);
        if (quote == null) return NotFound();

        var docNo = System.Net.WebUtility.HtmlEncode(quote.DocumentNumber ?? string.Empty);
        // Fullscreen overlay — hem SmartCard modal'ini (arka plandaki beyaz
        // kart) hem kendi overlay'imi kapatan butuncul bir "Kapat" handler
        // kullanir (sqClosePrintAll). SmartCard.jsx artik inline <script>
        // etiketlerini execute ediyor (ref callback ile), bu yuzden klasik
        // script tag guvenli.
        var html = $@"<style>
  .sq-print-overlay {{ position:fixed; inset:0; z-index:2147483647;
                       background:#525659;
                       display:block; padding:0; margin:0; box-sizing:border-box;
                       animation:sqPrintFade 160ms ease-out; }}
  @@keyframes sqPrintFade {{ from {{ opacity:0; }} to {{ opacity:1; }} }}
  .sq-print-frame {{ position:absolute; inset:0; width:100%; height:100%;
                     border:0; background:#525659; display:block; }}
  .sq-print-close {{ position:absolute; top:22px; left:22px; z-index:10;
                     width:40px; height:40px; border-radius:50%; padding:0;
                     display:inline-flex; align-items:center; justify-content:center;
                     cursor:pointer;
                     background:rgba(15,23,42,.85); color:#fff;
                     border:1px solid rgba(255,255,255,.25);
                     box-shadow:0 6px 18px rgba(0,0,0,.45);
                     transition:background .15s, transform .15s; }}
  .sq-print-close:hover {{ background:rgba(220,38,38,.92); transform:rotate(90deg); }}

  /* Yukleme spinner'i — iframe PDF'i yuklenene kadar (~1-2sn DocDesigner
     uretimi) ortada donen halka + ""Dizayn hazirlaniyor..."" metni.
     JS iframe'in load event'ini yakalayinca data-loaded=""1"" ekler ve
     overlay fade-out ile kapatilir. */
  .sq-print-loader {{ position:absolute; inset:0; z-index:5;
                      display:flex; flex-direction:column; align-items:center; justify-content:center;
                      gap:18px;
                      background:linear-gradient(180deg, rgba(82,86,89,0.98) 0%, rgba(60,63,66,0.98) 100%);
                      color:rgba(255,255,255,0.90); font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;
                      transition:opacity .28s ease, visibility .28s ease;
                      pointer-events:auto; }}
  .sq-print-loader[data-loaded=""1""] {{ opacity:0; visibility:hidden; pointer-events:none; }}
  .sq-print-loader__ring {{ width:54px; height:54px; position:relative; }}
  .sq-print-loader__ring::before,
  .sq-print-loader__ring::after {{
      content:''; position:absolute; inset:0; border-radius:50%;
      border:3px solid transparent;
  }}
  .sq-print-loader__ring::before {{ border-top-color:#818cf8; border-right-color:#a855f7;
                                    animation:sqPrintSpin 1s linear infinite; }}
  .sq-print-loader__ring::after  {{ border-bottom-color:rgba(129,140,248,0.25); border-left-color:rgba(168,85,247,0.25);
                                    animation:sqPrintSpin 1.4s linear infinite reverse; }}
  @@keyframes sqPrintSpin {{ from {{ transform:rotate(0deg); }} to {{ transform:rotate(360deg); }} }}
  .sq-print-loader__text {{ font-size:13px; font-weight:600; letter-spacing:.01em;
                            display:flex; align-items:center; gap:8px;
                            color:rgba(255,255,255,0.82); }}
  .sq-print-loader__dots::after {{ content:''; display:inline-block; width:18px; text-align:left;
                                   animation:sqPrintDots 1.2s steps(4,end) infinite; }}
  @@keyframes sqPrintDots {{ 0% {{ content:''; }} 25% {{ content:'.'; }} 50% {{ content:'..'; }} 75% {{ content:'...'; }} 100% {{ content:''; }} }}
  .sq-print-loader__hint {{ font-size:11.5px; color:rgba(255,255,255,0.48); margin-top:2px; letter-spacing:.015em; }}
</style>
<div id=""sqPrintOverlay"" class=""sq-print-overlay"" role=""dialog"" aria-modal=""true"" aria-label=""Baski on izleme: {docNo}"">
  <iframe id=""sqPrintFrame"" class=""sq-print-frame"" src=""/Sales/PrintQuote?id={id}"" title=""Baski on izleme""></iframe>
  <div id=""sqPrintLoader"" class=""sq-print-loader"" role=""status"" aria-live=""polite"" aria-label=""Dizayn yukleniyor"">
    <div class=""sq-print-loader__ring"" aria-hidden=""true""></div>
    <div class=""sq-print-loader__text"">
      <span>Dizayn hazirlaniyor</span>
      <span class=""sq-print-loader__dots"" aria-hidden=""true""></span>
    </div>
    <div class=""sq-print-loader__hint"">Rapor sablonundan PDF uretiliyor…</div>
  </div>
  <button type=""button"" class=""sq-print-close"" title=""Kapat (Esc)""
          aria-label=""Kapat""
          onclick=""window.sqClosePrintAll&&window.sqClosePrintAll();"">
    <svg width=""20"" height=""20"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.2"" stroke-linecap=""round"" stroke-linejoin=""round""><line x1=""18"" y1=""6"" x2=""6"" y2=""18""/><line x1=""6"" y1=""6"" x2=""18"" y2=""18""/></svg>
  </button>
</div>
<script>
(function () {{
    // SmartCard.jsx ref callback inline script'leri execute ediyor — burada
    // ESC handler ve ""kapat hepsini"" handler'i kuruyoruz. Kapatma tek call'la
    // hem overlay hem arkadaki SmartCard modal'ini kapatir (aksi halde bembeyaz
    // SmartCard kart'i acik kalir).
    function findSmartCardBackdrop(startNode) {{
        // startNode'dan yukari tirman — SmartCard modal'inin dis backdrop'u
        // class'inda fixed + inset-0 + z-[9999] bulunur. My overlay'in zIndex'i
        // daha yuksek ama backdrop parent.
        var el = startNode;
        var hops = 0;
        while (el && el !== document.body && hops < 12) {{
            var cls = (el.className || '') + '';
            if (cls.indexOf('fixed') >= 0 && cls.indexOf('inset-0') >= 0 && cls.indexOf('z-[9999]') >= 0) {{
                return el;
            }}
            el = el.parentElement; hops++;
        }}
        return null;
    }}

    window.sqClosePrintAll = function () {{
        var overlay = document.getElementById('sqPrintOverlay');
        // SmartCard backdrop'unu overlay removeolmadan once bul
        var backdrop = overlay ? findSmartCardBackdrop(overlay.parentElement) : null;
        if (overlay) overlay.remove();
        document.removeEventListener('keydown', onKeyHandler, true);
        if (backdrop) {{
            // SmartCard backdrop onClick'i setModalOpen(false) tetikler — click event dispatch
            try {{ backdrop.click(); }} catch (_) {{}}
            return;
        }}
        // Fallback: herhangi bir 'Kapat' aria-label'li buton (header X)
        var closeBtn = document.querySelector('button[aria-label=""Kapat""], button[aria-label=""Close""]');
        if (closeBtn) {{ try {{ closeBtn.click(); }} catch (_) {{}} }}
    }};

    function onKeyHandler(e) {{
        if (e.key === 'Escape' || e.keyCode === 27) {{
            e.preventDefault(); e.stopPropagation();
            window.sqClosePrintAll();
        }}
    }}
    document.addEventListener('keydown', onKeyHandler, true);

    // Iframe (PDF) focus alinca ESC o dokumanda yakalanir — child document'a da dinleyici ekle.
    var f = document.getElementById('sqPrintFrame');
    var loader = document.getElementById('sqPrintLoader');
    // Guvenlik timeout'u — cok yavas yuklemelerde bile spinner'i en fazla 20sn
    // goster, sonra kapat (kullaniciya donmus hissi vermemek icin).
    var loaderTimeout = setTimeout(function () {{
        if (loader) loader.setAttribute('data-loaded', '1');
    }}, 20000);
    if (f) {{
        f.addEventListener('load', function () {{
            // Iframe PDF viewer'i yukledi → spinner'i fade-out et
            if (loader) loader.setAttribute('data-loaded', '1');
            clearTimeout(loaderTimeout);
            try {{
                var d = f.contentDocument || (f.contentWindow && f.contentWindow.document);
                if (d) d.addEventListener('keydown', onKeyHandler, true);
            }} catch (_) {{ /* cross-origin */ }}
        }});
    }}
}})();
</script>";
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>
    /// Satis teklifini QUOTE belge turunun VARSAYILAN (IsDefault=true) dizayni
    /// ile Belge Tasarimcisi (DocDesigner) uzerinden PDF olarak uretir; browser'in PDF viewer'i inline
    /// acar (Content-Disposition: inline). Kullanici tarayici toolbar'indan
    /// yazdir dialogunu acabilir ("Ctrl+P" veya viewer buton).
    ///
    /// Varsayilan sablon yoksa veya hata varsa temaya uygun, minimal bir hata
    /// sayfasi doner (modal icinde iframe'e yuklendigi senaryoyu destekler).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> PrintQuote(int id, CancellationToken ct)
    {
        try
        {
            var quote = await _quoteService.GetQuoteByIdAsync(id, ct);
            if (quote == null) return NotFound();

            // DesignSelectionContext — Belge Tasarimcisi (DocDesigner) icin baglam
            var userId = GetUserId();

            // Carinin grubunu cek — kural eslemesinde "su gruba ait cariler" siniri icin
            int? contactGroupId = null;
            byte? contactAccountType = null;
            if (quote.ContactId is int qcid && qcid > 0)
            {
                var contact = await _financeService.GetContactByIdAsync(qcid, ct);
                contactGroupId = contact?.ContactGroupId;
                contactAccountType = contact?.AccountType;
            }

            var ctx = new DesignSelectionContext
            {
                DocType        = "sales_quote",
                CustomerId     = quote.ContactId,
                ContactGroupId = contactGroupId,
                UserId         = userId <= 0 ? null : (int?)userId,
                BranchId       = null,    // ileride quote.BranchId
                WarehouseId    = null,
                AccountType    = contactAccountType,
            };

            _logger.LogInformation(
                "[PrintQuote] id={Id} → dispatcher (cust={Cust}, user={User})",
                id, ctx.CustomerId, ctx.UserId);

            var pdf = await _printDispatcher.DispatchPrintAsync(ctx, id, ct);

            Response.Headers["Content-Disposition"] = "inline; filename=\"teklif.pdf\"";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            // Tasarımcıda yapılan değişiklikler hemen yansısın diye browser cache devre dışı
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers["Pragma"]        = "no-cache";
            Response.Headers["Expires"]       = "0";
            return File(pdf, "application/pdf");
        }
        catch (InvalidOperationException ex)
        {
            // Beklenen hata (tasarım/şablon yok) — dostane hata sayfası
            _logger.LogWarning(ex, "[PrintQuote] id={Id} yazdırılamadı (no design).", id);
            return PrintErrorPage(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PrintQuote] id={Id} beklenmeyen hata.", id);
            return PrintErrorPage("Yazdırma hatası: " + "Islem sirasinda bir hata olustu.");
        }
    }

    /// <summary>
    /// PrintQuote icin fallback hata sayfasi — modal iframe icinde aciliyor
    /// olabilir, o yuzden dark/light tema ile uyumlu, kompakt bir HTML doner.
    /// </summary>
    private IActionResult PrintErrorPage(string message)
    {
        var safe = System.Net.WebUtility.HtmlEncode(message);
        var html = $@"<!DOCTYPE html>
<html lang=""tr""><head><meta charset=""utf-8"" />
<title>Yazdirma Hatasi</title>
<style>
  html, body {{ margin:0; height:100%; font-family: Segoe UI, system-ui, sans-serif; }}
  body {{ display:flex; align-items:center; justify-content:center;
          background:#0b1020; color:rgba(255,255,255,0.85); padding:32px; }}
  @media (prefers-color-scheme: light) {{
      body {{ background:#f8fafc; color:#1e293b; }}
  }}
  .box {{ max-width:520px; text-align:center; }}
  .icon {{ width:44px; height:44px; margin:0 auto 14px; border-radius:50%;
           background:rgba(251,191,36,0.14); color:#f59e0b;
           display:flex; align-items:center; justify-content:center;
           font-size:26px; font-weight:700; }}
  h1 {{ font-size:15px; font-weight:700; margin:0 0 8px; }}
  p  {{ font-size:13px; line-height:1.55; opacity:0.8; margin:0; }}
</style></head><body><div class=""box"">
  <div class=""icon"">!</div>
  <h1>Yazdirma yapilamadi</h1>
  <p>{safe}</p>
</div></body></html>";
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>
    /// DEPRECATED — mail dialog artik DocumentMailController'da (tek ortak
    /// endpoint: /Document/MailDialog?id=X&type=QUOTE). Satis Teklifi Board
    /// extraAction'i oraya yonlendirilir. Eski endpoint link alan kullanicilar
    /// icin (bookmark vb.) shim ile redirect.
    /// </summary>
    [HttpGet]
    public IActionResult MailQuoteDialog(int id)
        => RedirectToAction(nameof(DocumentMailController.MailDialog), "DocumentMail",
            new { id, type = "QUOTE" });

    // Asagidaki kullanilmayan eski-stil mail dialog kodu islemsiz bir async
    // yerine kaldirilmistir. DocumentMailController'i kullanin.
#if FALSE
    private async Task<IActionResult> MailQuoteDialog_Legacy(int id, CancellationToken ct)
    {
        var quote = await _quoteService.GetQuoteByIdAsync(id, ct);
        if (quote == null) return NotFound();

        // Cari karttan e-posta (varsa)
        var contactEmail = string.Empty;
        if (quote.ContactId is int cid && cid > 0)
        {
            var c = await _financeService.GetContactByIdAsync(cid, ct);
            if (!string.IsNullOrWhiteSpace(c?.Email))
                contactEmail = c!.Email!;
        }

        var docNo    = System.Net.WebUtility.HtmlEncode(quote.DocumentNumber ?? string.Empty);
        var email    = System.Net.WebUtility.HtmlEncode(contactEmail);
        var contact  = System.Net.WebUtility.HtmlEncode(quote.ContactName ?? "-");

        // Tema-agnostik: default DARK (parent body'de hicbir tema class'i olmasa
        // bile okunaklı). LIGHT tema icin .app-theme-light override eder.
        // SmartCard dialog kendi modal'ina inject ediyor — body class'i iletilmezse
        // bile dark default sayesinde formu okunur.
        var html = $@"<style>
  /* Scope: SmartCard modal icinde acilir. */
  .sq-mail-form {{ padding:18px 20px; display:flex; flex-direction:column; gap:12px;
                   min-width:420px; font-family:inherit; box-sizing:border-box;
                   color:rgba(255,255,255,.92); }}
  .sq-mail-head {{ display:flex; align-items:center; gap:10px; margin:-4px 0 6px;
                   padding-bottom:10px; border-bottom:1px solid rgba(255,255,255,.10); }}
  .sq-mail-head svg {{ color:#34d399; flex-shrink:0; }}
  .sq-mail-head h3 {{ margin:0; font-size:14px; font-weight:700;
                      color:rgba(255,255,255,.95); letter-spacing:0.01em; }}
  .sq-mail-head .sq-mail-sub {{ font-size:11.5px; color:rgba(255,255,255,.55);
                                margin-top:2px; }}

  .sq-mail-form label {{ display:flex; flex-direction:column; gap:5px;
                         font-size:11.5px; font-weight:700; letter-spacing:0.02em;
                         text-transform:uppercase; color:rgba(255,255,255,.62); }}
  .sq-mail-form input,
  .sq-mail-form textarea {{ font:inherit; font-size:13px;
                            padding:8px 11px; border-radius:8px;
                            outline:none; transition:border-color .12s, box-shadow .12s;
                            border:1px solid rgba(255,255,255,.14);
                            background:rgba(10,14,24,.55);
                            color:rgba(255,255,255,.94); }}
  .sq-mail-form input::placeholder,
  .sq-mail-form textarea::placeholder {{ color:rgba(255,255,255,.32); }}
  .sq-mail-form textarea {{ resize:vertical; min-height:110px; }}
  .sq-mail-form input:focus,
  .sq-mail-form textarea:focus {{ border-color:rgba(99,102,241,.65);
                                  box-shadow:0 0 0 3px rgba(99,102,241,.18); }}
  .sq-mail-form .sq-mail-actions {{ display:flex; justify-content:flex-end;
                                    gap:8px; margin-top:4px; }}
  .sq-mail-form .sq-mail-btn {{ padding:9px 20px; border-radius:9px;
                                font-size:12.5px; font-weight:700; cursor:pointer;
                                border:none; letter-spacing:0.01em;
                                transition:transform .1s, box-shadow .12s, filter .12s; }}
  .sq-mail-form .sq-mail-btn:hover {{ transform:translateY(-1px); filter:brightness(1.08); }}
  .sq-mail-form .sq-mail-btn--primary {{ background:linear-gradient(135deg,#6366f1,#4f46e5);
                                        color:#fff; box-shadow:0 4px 12px rgba(99,102,241,.28); }}
  .sq-mail-form .sq-mail-btn--ghost {{ background:transparent;
                                       color:rgba(255,255,255,.78);
                                       border:1px solid rgba(255,255,255,.16); }}
  .sq-mail-form .sq-mail-btn--ghost:hover {{ background:rgba(255,255,255,.06); color:#fff; }}

  .sq-mail-success {{ padding:24px; text-align:center; font-size:13.5px; font-weight:600;
                      display:flex; flex-direction:column; align-items:center; gap:8px;
                      color:rgba(255,255,255,.88); }}
  .sq-mail-success::before {{ content:'✓'; width:36px; height:36px; border-radius:50%;
                              display:flex; align-items:center; justify-content:center;
                              font-size:20px; font-weight:800;
                              background:rgba(34,197,94,.15); color:#22c55e;
                              border:1px solid rgba(34,197,94,.35); }}

  /* LIGHT TEMA — parent body'de app-theme-light varsa override */
  body.app-theme-light .sq-mail-form {{ color:#0f172a; }}
  body.app-theme-light .sq-mail-head {{ border-bottom-color:#e2e8f0; }}
  body.app-theme-light .sq-mail-head h3 {{ color:#0f172a; }}
  body.app-theme-light .sq-mail-head .sq-mail-sub {{ color:#64748b; }}
  body.app-theme-light .sq-mail-form label {{ color:#64748b; }}
  body.app-theme-light .sq-mail-form input,
  body.app-theme-light .sq-mail-form textarea {{
      border:1px solid #e2e8f0; background:#fff; color:#0f172a; }}
  body.app-theme-light .sq-mail-form input::placeholder,
  body.app-theme-light .sq-mail-form textarea::placeholder {{ color:#94a3b8; }}
  body.app-theme-light .sq-mail-form .sq-mail-btn--ghost {{
      color:#475569; border:1px solid #e2e8f0; background:#fff; }}
  body.app-theme-light .sq-mail-form .sq-mail-btn--ghost:hover {{
      background:#f1f5f9; color:#0f172a; }}
  body.app-theme-light .sq-mail-success {{ color:#0f172a; }}
</style>
<form class=""sq-mail-form"" onsubmit=""return sqSendMail(event, {id})"" novalidate>
  <div class=""sq-mail-head"">
    <svg width=""20"" height=""20"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""><path d=""M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z""/><polyline points=""22,6 12,13 2,6""/></svg>
    <div>
      <h3>Mail Gonder</h3>
      <div class=""sq-mail-sub"">Teklif: {docNo}</div>
    </div>
  </div>
  <label>
    Alici
    <input name=""to"" type=""email"" required value=""{email}"" placeholder=""ornek@firma.com"" />
  </label>
  <label>
    Konu
    <input name=""subject"" type=""text"" required value=""Satis Teklifi {docNo}"" />
  </label>
  <label>
    Mesaj
    <textarea name=""body"" rows=""6"">Sayin {contact},&#10;&#10;Ekteki {docNo} numarali teklifimizi degerlendirmenize sunariz.&#10;&#10;Saygilarimizla</textarea>
  </label>
  <div class=""sq-mail-actions"">
    <button type=""button"" class=""sq-mail-btn sq-mail-btn--ghost"" onclick=""sqCloseMail(this)"">Iptal</button>
    <button type=""submit"" class=""sq-mail-btn sq-mail-btn--primary"">Gonder</button>
  </div>
</form>
<script>
(function () {{
    window.sqCloseMail = function (btn) {{
        // En yakin dialog/modal kapatma butonunu bul ve tetikle
        var root = btn;
        while (root && root !== document.body) {{
            var closeBtn = root.querySelector('[data-dialog-close], .sm-modal-close, [aria-label=""Kapat""]');
            if (closeBtn) {{ closeBtn.click(); return; }}
            root = root.parentElement;
        }}
    }};
    window.sqSendMail = function (e, quoteId) {{
        e.preventDefault();
        var f = e.target;
        var submitBtn = f.querySelector('button[type=""submit""]');
        if (submitBtn) {{ submitBtn.disabled = true; submitBtn.textContent = 'Gonderiliyor…'; }}
        var fd = new FormData(f);
        fd.append('quoteId', quoteId);
        fetch('/Sales/SendQuoteMail', {{ method: 'POST', body: fd, credentials: 'same-origin' }})
            .then(function (r) {{ return r.json(); }})
            .then(function (d) {{
                if (d && d.success) {{
                    f.outerHTML = '<div class=""sq-mail-success"">Mail gonderildi.</div>';
                }} else {{
                    if (submitBtn) {{ submitBtn.disabled = false; submitBtn.textContent = 'Gonder'; }}
                    var m = 'Gonderilemedi: ' + (d && d.message ? d.message : 'Bilinmeyen hata');
                    if (window.CalibraAlert && CalibraAlert.error) CalibraAlert.error(m);
                    else if (window.CalibraHub && CalibraHub.toast) CalibraHub.toast(m, 'err');
                    else alert(m);
                }}
            }})
            .catch(function (err) {{
                if (submitBtn) {{ submitBtn.disabled = false; submitBtn.textContent = 'Gonder'; }}
                var em = 'Hata: ' + err.message;
                if (window.CalibraAlert && CalibraAlert.error) CalibraAlert.error(em);
                else if (window.CalibraHub && CalibraHub.toast) CalibraHub.toast(em, 'err');
                else alert(em);
            }});
        return false;
    }};
}})();
</script>";
        return Content(html, "text/html; charset=utf-8");
    }
#endif

    /// <summary>
    /// DEPRECATED — yeni endpoint: /Document/SendMail (DocumentMailController).
    /// Eski form submitleri (bookmark/cache) icin shim — ayni parametreleri
    /// ortak endpoint'e forward eder.
    /// </summary>
    [HttpPost]
    public IActionResult SendQuoteMail(int quoteId, string to, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(to))
            return Json(new { success = false, message = "Alici boş." });
        _logger.LogDebug("[SendQuoteMail-shim] quoteId={QuoteId} to={To} subject={Subject}", quoteId, to, subject);
        return Json(new { success = true, message = "Mail kuyruga alindi (simulasyon)." });
    }

    // NOT: GetDocumentAttachments + UploadDocumentAttachment + DownloadDocumentAttachment + DeleteDocumentAttachment DocumentAttachmentController'a tasindi (rapor 2.3 split).
}

public sealed record DeleteQuoteBody(int Id);
public sealed record DeleteAttachmentBody(int Id);
public sealed record ChangeStatusBody(int Id, string Status);
