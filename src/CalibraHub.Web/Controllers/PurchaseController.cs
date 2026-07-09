using CalibraHub.Application.Constants;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Web.Models;
using CalibraHub.Web.Models.Purchase;
using CalibraHub.Web.Models.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Satin Alma — 3 asamali (Talep / Teklif / Siparis) modulun controller'i.
///
/// 2026-05-22: Menu iskeleti + DB seed.
/// 2026-05-23: Edit ekrani Sales/DocumentEdit view'ini paylasiyor.
/// 2026-05-23: Liste sayfalari — SalesController.Quotes pattern'i ile SmartBoard.
/// Document tablosu uzerinden DocumentTypeId ile filtrelenir.
/// 2026-05-23: FulfillmentCenter — İhtiyaç karsilama merkezi (transfer/teklif/siparis).
/// </summary>
[Authorize]
public sealed class PurchaseController : Controller
{
    private readonly IDocumentService _documentService;
    private readonly IDocumentRepository _documentRepo;
    private readonly IDocumentTypeRepository _documentTypeRepo;
    private readonly IWidgetService _widgetService;
    private readonly IDocumentSourceRepository _docSourceRepo;
    private readonly IStockDocRepository _stockDocRepo;
    private readonly ILogisticsConfigurationService _logisticsService;
    private readonly ICompanyParameterService _companyParams;
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IUserSettingRepository _userSettingRepo;
    private readonly string _schema;
    private const string FlatColCfgKey = "ui.fc3.col-cfg-flat";

    public PurchaseController(
        IDocumentService documentService,
        IDocumentRepository documentRepo,
        IDocumentTypeRepository documentTypeRepo,
        IWidgetService widgetService,
        IDocumentSourceRepository docSourceRepo,
        IStockDocRepository stockDocRepo,
        ILogisticsConfigurationService logisticsService,
        ICompanyParameterService companyParams,
        SqlServerConnectionFactory connectionFactory,
        IUserSettingRepository userSettingRepo,
        CalibraDatabaseOptions dbOptions)
    {
        _documentService   = documentService;
        _documentRepo      = documentRepo;
        _documentTypeRepo  = documentTypeRepo;
        _widgetService     = widgetService;
        _docSourceRepo     = docSourceRepo;
        _stockDocRepo      = stockDocRepo;
        _logisticsService  = logisticsService;
        _companyParams     = companyParams;
        _connectionFactory = connectionFactory;
        _userSettingRepo   = userSettingRepo;
        _schema = string.IsNullOrWhiteSpace(dbOptions.Schema) ? "dbo" : dbOptions.Schema.Trim();
    }

    private int? CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>
    /// Stok etkisi kapalı (STOCK_EFFECT_{code}=false) belge türleri için SQL filtre
    /// parçası üretir. Bakiye sorgularında Document alias'ına eklenir; parametre
    /// tanımsızken boş döner (filtre yok = mevcut davranış).
    /// </summary>
    private async Task<(string Filter, List<SqlParameter> Parameters)> BuildStockEffectFilterAsync(
        string docAlias, CancellationToken ct)
    {
        var ids = await CalibraHub.Application.Services.StockEffectHelper.GetDisabledDocTypeIdsAsync(
            _companyParams, _documentTypeRepo, ct);
        if (ids.Count == 0) return ("", []);

        var names  = string.Join(",", ids.Select((_, i) => $"@sef{i}"));
        var filter = $" AND ({docAlias}.[DocumentTypeId] IS NULL OR {docAlias}.[DocumentTypeId] NOT IN ({names}))";
        var prms   = ids.Select((id, i) => new SqlParameter($"@sef{i}", id)).ToList();
        return (filter, prms);
    }

    /// <summary>
    /// Karşılama aksiyonlarının onay şartını tek kaynaktan çözer: İhtiyaç Kaydı türünde
    /// onay tetikleme AÇIKsa (APPROVAL_ENABLED_PurchaseRequest != "false") yalnızca Onaylı
    /// belgeler karşılanabilir; KAPALIysa şart uygulanmaz (tüm belgeler karşılanabilir).
    /// Ayrı bir "karşılamada onay şartı" parametresi yoktur — davranış doğrudan onay
    /// parametresine bağlıdır (2026-07-08 sadeleştirme).
    /// Hata varsa kullanıcıya gösterilecek mesaj, yoksa null döner.
    /// </summary>
    private async Task<string?> CheckFulfillmentApprovalGuardAsync(
        IEnumerable<int> documentIds, CancellationToken ct)
    {
        var kindEnabled = await _companyParams.GetStringAsync(
            ApprovalParameters.FormCode, ApprovalParameters.EnabledKey("PurchaseRequest"), ct) != "false";

        // Açık (Pending) onay süreci olan belgeler — parametre sonradan kapatılsa bile önce
        // onay akışını tamamlamalı. Bu kontrol her durumda uygulanır.
        var pendingIds = (await GetPendingApprovalDocIdsAsync(ct)).ToHashSet();

        foreach (var id in documentIds.Distinct())
        {
            var doc = await _documentService.GetQuoteByIdAsync(id, ct);
            if (doc == null) continue;

            if (pendingIds.Contains(id))
                return $"{doc.DocumentNumber} onay sürecinde — karşılanmadan önce onay tamamlanmalı.";

            // Onay tetikleme açıksa yalnızca Onaylı belgeler karşılanabilir.
            if (kindEnabled && !string.Equals(doc.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                return $"{doc.DocumentNumber} onaylanmadan karşılama yapılamaz (durum: {TranslateStatus(doc.Status)}). " +
                       "İhtiyaç kayıtları onay akışına tabidir; belge onaylandıktan sonra karşılanabilir.";
        }
        return null;
    }

    /// <summary>
    /// Karşılama fişinden (transfer / ambar çıkış / FIFO) kaynak İhtiyaç belgelerine
    /// DocumentSource soyağacı kenarı yazar: türetilen = fiş (targetDocId), kaynak = İhtiyaç.
    /// "İlişkili Belgeler / Akış" görünümü bu kenarlardan beslenir. Idempotent (UNIQUE INDEX).
    /// </summary>
    private async Task LinkFulfillmentSourcesAsync(int targetDocId, IReadOnlyList<int>? sourceDocIds, CancellationToken ct)
    {
        if (targetDocId <= 0 || sourceDocIds is not { Count: > 0 }) return;
        await _docSourceRepo.EnsureSchemaAsync(ct);
        foreach (var rid in sourceDocIds.Distinct())
            if (rid > 0) await _docSourceRepo.AddAsync(targetDocId, rid, ct);
    }

    [HttpGet("/Purchase/Requests")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseRequest)]
    public Task<IActionResult> Requests(CancellationToken ct) =>
        RenderListAsync("alis_talebi",   "PURCHASE_REQUEST_EDIT", "İhtiyaç Kayıtları",
                        "ihtiyaç",       "/Purchase/Edit?type=purchase_request", "amber", ct);

    [HttpGet("/Purchase/Quotes")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseQuote)]
    public Task<IActionResult> Quotes(CancellationToken ct) =>
        RenderListAsync("alis_teklifi",  "PURCHASE_QUOTE_EDIT",   "Satin Alma Teklifleri",
                        "teklif",        "/Purchase/Edit?type=purchase_quote",   "blue",  ct);

    [HttpGet("/Purchase/Orders")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseOrder)]
    public Task<IActionResult> Orders(CancellationToken ct) =>
        RenderListAsync("alis_siparisi", "PURCHASE_ORDER_EDIT",   "Satin Alma Siparisleri",
                        "siparis",       "/Purchase/Edit?type=purchase_order",   "emerald", ct);

    [HttpGet("/Purchase/PurchaseDemands")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseDemand)]
    public Task<IActionResult> PurchaseDemands(CancellationToken ct) =>
        RenderListAsync("satin_alma_talebi", "PURCHASE_DEMAND_EDIT", "Satın Alma Talepleri",
                        "talep", "/Purchase/Edit?type=purchase_demand", "violet", ct,
                        newUrl: "/Purchase/PurchaseRequestWizard");

    [HttpGet("/Purchase/RequestsBoardConfig")]
    public async Task<IActionResult> RequestsBoardConfig(CancellationToken ct)
    {
        var config = await BuildPurchaseBoardAsync(
            "alis_talebi", "PURCHASE_REQUEST_EDIT", "İhtiyaç Kayıtları",
            "ihtiyaç", "/Purchase/Edit?type=purchase_request", "amber", ct);
        return Json(config);
    }

    [HttpGet("/Purchase/PurchaseDemandsBoardConfig")]
    public async Task<IActionResult> PurchaseDemandsBoardConfig(CancellationToken ct)
    {
        var config = await BuildPurchaseBoardAsync(
            "satin_alma_talebi", "PURCHASE_DEMAND_EDIT", "Satın Alma Talepleri",
            "talep", "/Purchase/Edit?type=purchase_demand", "violet", ct,
            newUrl: "/Purchase/PurchaseRequestWizard");
        return Json(config);
    }

    /// <summary>Liste view'ini render eden ortak helper — Sales/Documents.cshtml paylasilir.</summary>
    private async Task<IActionResult> RenderListAsync(
        string typeCode, string formCode, string title, string entityWord,
        string editUrl, string iconColor, CancellationToken ct, string? newUrl = null)
    {
        var boardConfig = await BuildPurchaseBoardAsync(
            typeCode, formCode, title, entityWord, editUrl, iconColor, ct, newUrl);
        // Documents.cshtml ViewData["Title"]'i hardcode "Satis Teklifleri" yaziyor;
        // controller'dan override ile dogru baslik gosteriyoruz (tarayici tab + page header).
        ViewData["Title"]    = title;
        ViewData["FormCode"] = formCode;
        ViewData["DbInfo"]   = null;  // sales-spesifik HTML tablosunu gizle
        return View("~/Views/Sales/Documents.cshtml", new DocumentsViewModel
        {
            AvailableColumns = Array.Empty<CalibraHub.Web.Models.Logistics.GridColumnDefinition>(),
            VisibleColumns   = Array.Empty<string>(),
            BoardConfig      = boardConfig,
        });
    }

    /// <summary>
    /// Satin alma edit/new ekrani. Logic SalesController.DocumentEdit ile ortak —
    /// internal redirect ile delegate. View `DocumentTypeFormMap.Resolve` ile dogru
    /// form kodlarini cozer (PURCHASE_REQUEST_EDIT / PURCHASE_QUOTE_EDIT / ...).
    /// </summary>
    [HttpGet("/Purchase/Edit")]
    public IActionResult Edit(int? id, string? type, int? fromRequest)
    {
        var t = (type ?? "").Trim().ToLowerInvariant() switch
        {
            "purchase_quote"   or "alis_teklifi"       => "purchase_quote",
            "purchase_order"   or "alis_siparisi"      => "purchase_order",
            "purchase_demand"  or "satin_alma_talebi"  => "purchase_demand",
            _                                           => "purchase_request",
        };
        var idQ  = id.HasValue && id.Value > 0 ? $"&id={id.Value}" : "";
        var frQ  = fromRequest.HasValue && fromRequest.Value > 0 ? $"&fromRequest={fromRequest.Value}" : "";
        return Redirect($"/Sales/DocumentEdit?type={t}{idQ}{frQ}");
    }

    /// <summary>
    /// Satin alma listesi SmartBoard config'i. SalesController.BuildQuotesBoardConfig
    /// pattern'iyle ozdes; sadece "Siparise Donustur" / "Mail Gonder" gibi sales-spesifik
    /// extra action'lar yok (ileriki sprint'te Talep->Teklif->Siparis donusumu icin eklenecek).
    /// </summary>
    private async Task<object> BuildPurchaseBoardAsync(
        string typeCode, string formCode, string title, string entityWord, string editUrl, string iconColor, CancellationToken ct, string? newUrl = null)
    {
        var docs = await _documentService.GetByTypeAsync(typeCode, search: null, status: null, ct);
        var trCulture = CultureInfo.GetCultureInfo("tr-TR");

        // Master widget sablonu (Forms tablosundaki admin-tanimli widget'lar — grup/grid haric).
        // 2026-05-24: Dictionary serialization + Standart Alanlar grubu (filtre panel collapsible).
        var masterWidgets = new List<object>();
        var schema = await _widgetService.GetFormSchemaByCodeAsync(formCode, ct);
        if (schema != null)
        {
            foreach (var w in schema.Widgets.Where(w => w.IsActive
                && !string.Equals(w.DataType, "group", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(w.DataType, "grid",  StringComparison.OrdinalIgnoreCase)))
            {
                var dt = w.DataType.ToLowerInvariant();
                // Dropdown / Coklu Secim widget options → {value,label} (filter combobox icin)
                object? optionsArray = null;
                if ((dt == "dropdown" || dt == "multi-select" || dt == "multi_select" || dt == "multiselect")
                    && w.Options != null && w.Options.Count > 0)
                {
                    optionsArray = w.Options.Select(s => (object)new Dictionary<string, object?> {
                        ["value"] = s, ["label"] = s,
                    }).ToList();
                }
                var wd = new Dictionary<string, object?>
                {
                    ["id"]           = w.WidgetCode,
                    ["dbId"]         = w.Id,
                    ["isPlainField"] = w.IsPlainField,
                    ["type"]         = "data",
                    ["dataType"]     = dt,
                    ["label"]        = w.Label,
                    ["source"]       = "widget",
                };
                if (optionsArray != null) wd["options"] = optionsArray;
                masterWidgets.Add(wd);
            }
        }

        // Sistem alanlari — "Standart Alanlar" grubunda collapsible
        const string STD_GROUP = "standardalanlar";
        const string STD_LBL   = "Standart Alanlar";
        Dictionary<string, object?> MakeStdWidget(string id, string label, string dataType)
            => new Dictionary<string, object?>
            {
                ["id"]           = id,
                ["dbId"]         = (int?)null,
                ["isPlainField"] = false,
                ["type"]         = "data",
                ["dataType"]     = dataType,
                ["label"]        = label,
                ["source"]       = "standard",
                ["group"]        = STD_GROUP,
                ["groupLabel"]   = STD_LBL,
            };
        masterWidgets.Add(MakeStdWidget("w_tutar", "Toplam Tutar",   "currency"));
        // Durum — multi-select combobox; options bilinen durum kodlarinin TR cevirisinden
        var statusOptions = new[] { "Taslak", "Gonderildi", "Onaylandi", "Reddedildi", "Iptal", "Kapali" }
            .Select(s => (object)new Dictionary<string, object?> { ["value"] = s, ["label"] = s })
            .ToList();
        var durumWidget = MakeStdWidget("w_durum", "Durum", "options");
        durumWidget["options"] = statusOptions;
        masterWidgets.Add(durumWidget);
        masterWidgets.Add(MakeStdWidget("w_kalem", "Kalem Sayısı",   "numeric"));
        masterWidgets.Add(MakeStdWidget("w_tarih", "Tarih",          "date"));

        // Batch widget degerleri — tum belgeler icin tek sorgu (N+1 yok)
        var recordIds = docs.Select(d => d.Id.ToString()).ToArray();
        var batchWidgets = masterWidgets.Count > 0 && recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync(formCode, recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        // İhtiyaç Kaydı onay tetikleme açık mı — "Onaya Gönder" butonu yalnızca açıkken gösterilir.
        // (Liste boyunca sabit; döngü öncesi tek kez okunur.)
        var purchaseReqApprovalEnabled = !string.Equals(typeCode, "alis_talebi", StringComparison.OrdinalIgnoreCase)
            || await _companyParams.GetStringAsync(
                   ApprovalParameters.FormCode, ApprovalParameters.EnabledKey("PurchaseRequest"), ct) != "false";

        var entities = new List<object>();
        foreach (var doc in docs)
        {
            var widgets = new List<object>
            {
                // Sistem widget'lari — Sales pattern'i ile ayni alanlar (purchase belgesinde
                // de toplam tutar, durum, kalem sayisi, tarih anlam tasir).
                new { id = "w_tutar", type = "data", dataType = "currency", label = "Toplam Tutar",
                      value = doc.GrandTotal.ToString("N2", trCulture),
                      detail = doc.CurrencyCode ?? "TRY", color = "blue",
                      alwaysVisible = true },
                new { id = "w_durum", type = "data", dataType = "options", label = "Durum",
                      value = TranslateStatus(doc.Status), detail = (string?)null,
                      color = StatusColor(doc.Status),
                      alwaysVisible = true },
                new { id = "w_kalem", type = "data", dataType = "numeric", label = "Kalem Sayisi",
                      value = doc.LineCount.ToString(CultureInfo.InvariantCulture),
                      detail = "kalem", color = "slate",
                      alwaysVisible = true },
                new { id = "w_tarih", type = "data", dataType = "date", label = "Tarih",
                      value = doc.DocumentDate.ToString("dd.MM.yyyy", trCulture),
                      detail = (string?)null, color = "slate",
                      alwaysVisible = true },
            };

            // İhtiyaç Kaydı — karşılama özet widget'ı
            if (string.Equals(typeCode, "alis_talebi", StringComparison.OrdinalIgnoreCase)
                && (doc.FulfillPending > 0 || doc.FulfillPartial > 0 || doc.FulfillFull > 0))
            {
                string karsilamaLabel; string karsilamaColor;
                if (doc.FulfillFull == doc.LineCount && doc.LineCount > 0)
                { karsilamaLabel = "Tamamlandı"; karsilamaColor = "emerald"; }
                else if (doc.FulfillPartial > 0 || doc.FulfillFull > 0)
                { karsilamaLabel = $"Kısmen ({doc.FulfillFull + doc.FulfillPartial}/{doc.LineCount})"; karsilamaColor = "amber"; }
                else
                { karsilamaLabel = $"Bekliyor ({doc.FulfillPending}/{doc.LineCount})"; karsilamaColor = "slate"; }
                widgets.Add(new { id = "w_karsilama", type = "data", dataType = "text",
                    label = "Karşılama", value = karsilamaLabel,
                    detail = (string?)null, color = karsilamaColor, alwaysVisible = true });
            }

            // Dinamik widget degerleri (WidgetTra)
            var recordId = doc.Id.ToString();
            if (batchWidgets.TryGetValue(recordId, out var renderDtos))
            {
                foreach (var w in renderDtos)
                {
                    widgets.Add(new
                    {
                        id           = w.WidgetId,
                        type         = "data",
                        dataType     = w.DataType.ToLowerInvariant(),
                        label        = w.Label,
                        value        = w.Value,
                        isPlainField = w.IsPlainField,
                    });
                }
            }

            string cardTitle;
            if (string.Equals(typeCode, "alis_talebi", StringComparison.OrdinalIgnoreCase))
            {
                cardTitle = !string.IsNullOrWhiteSpace(doc.RequesterPersonnelName)
                    ? doc.RequesterPersonnelName!
                    : "(talep edensiz)";
            }
            else
            {
                cardTitle = string.IsNullOrWhiteSpace(doc.ContactName) ? "(tedarikcisiz)" : doc.ContactName!;
            }

            // İhtiyaç kartlarında "Onaya Gönder" extra aksiyon — diğer tiplerde yok.
            // Karşılama işlemi artık header "Karşılama Merkezi" butonu → FulfillmentCenter ekranından yapılır.
            var extraActionsList = new List<object>();
            if (string.Equals(typeCode, "alis_talebi", StringComparison.OrdinalIgnoreCase) && purchaseReqApprovalEnabled)
            {
                // "Onaya Gönder" — Draft belgeler için aktif, diğerleri için pasif (disabled).
                // Onay tetikleme kapalıysa buton hiç gösterilmez (elle de onaya sokulamaz).
                var isDraft = string.Equals(doc.Status, "Draft", StringComparison.OrdinalIgnoreCase);
                extraActionsList.Add(new
                {
                    type           = "api-post",
                    label          = "Onaya Gönder",
                    icon           = "Send",
                    color          = "emerald",
                    url            = "/ApprovalFlow/StartByDocument?documentId={id}",
                    confirm        = $"{doc.DocumentNumber} numaralı ihtiyaç kaydını onaya göndermek istiyor musunuz?",
                    confirmOkLabel = "Evet, Gönder",
                    confirmVariant = "primary",
                    disabled       = !isDraft,
                });
            }
            var extraActions = extraActionsList.ToArray();

            entities.Add(new
            {
                id          = doc.Id,
                title       = cardTitle,
                subtitle    = doc.DocumentNumber ?? string.Empty,
                description = string.Empty,
                imageUrl    = (string?)null,
                statusBadge = (object?)null,
                widgets,
                primaryAction = new
                {
                    label      = "Duzenle",
                    icon       = "Edit",
                    color      = "amber",
                    url        = $"{editUrl}&id={doc.Id}",
                    hideButton = true,
                },
                secondaryAction = new
                {
                    label   = "Sil",
                    icon    = "Trash2",
                    apiUrl  = $"/Sales/DeleteDocumentJson?id={doc.Id}",
                    confirm = $"Bu {entityWord}i silmek istediginizden emin misiniz? ({doc.DocumentNumber})",
                },
                extraActions,
            });
        }

        var effectiveNewUrl = newUrl ?? editUrl;
        // alis_talebi listesinde "Karsilama Merkezi" header butonu eklenir.
        var boardActions = string.Equals(typeCode, "alis_talebi", StringComparison.OrdinalIgnoreCase)
            ? new object[]
            {
                new { id = "center", label = "Karşılama Merkezi", icon = "Layers", variant = "secondary", url = "/Purchase/FulfillmentCenter" },
                new { id = "new",    label = $"Yeni {Capitalize(entityWord)}", icon = "Plus", variant = "primary", url = effectiveNewUrl },
            }
            : (object[])new object[]
            {
                new { id = "new", label = $"Yeni {Capitalize(entityWord)}", icon = "Plus", variant = "primary", url = effectiveNewUrl },
            };

        var refreshUrl = typeCode.ToLowerInvariant() switch
        {
            "alis_talebi"       => "/Purchase/RequestsBoardConfig",
            "satin_alma_talebi" => "/Purchase/PurchaseDemandsBoardConfig",
            _ => (string?)null,
        };

        return new
        {
            boardKey          = $"purchase-{typeCode}",
            title,
            subtitle          = $"{entities.Count} {entityWord}",
            icon              = "ShoppingBag",
            iconColor,
            refreshUrl,
            searchPlaceholder = $"Hizli ara... ({entityWord} no, tedarikci)",
            emptyText         = $"Henuz {entityWord} olusturulmamis",
            actions           = boardActions,
            masterWidgets,
            entities,
        };
    }

    /// <summary>
    /// İhtiyaç Kaydı karşılama modal içeriği (partial HTML).
    /// SmartCard fetch-modal ile yüklenir: GET /Purchase/FulfillModal?requestId={id}
    /// Yanıt: belge başlığı + kalemler + mevcut bağlı belgeler + aksiyon butonları.
    /// </summary>
    [HttpGet("/Purchase/FulfillModal")]
    public async Task<IActionResult> FulfillModal(int requestId, CancellationToken ct)
    {
        if (requestId <= 0)
            return Content("<p style='color:#f87171;padding:12px;'>Geçersiz İhtiyaç Kaydı ID.</p>", "text/html");

        var requestDoc = await _documentService.GetQuoteByIdAsync(requestId, ct);
        if (requestDoc == null)
            return Content("<p style='color:#f87171;padding:12px;'>İhtiyaç Kaydı bulunamadı.</p>", "text/html");

        var lines      = await _documentService.GetQuoteLinesAsync(requestId, ct);
        var derivedIds = await _docSourceRepo.GetDerivedDocumentIdsAsync(requestId, ct);

        var derivedDocs = new List<DocumentDto>();
        foreach (var did in derivedIds)
        {
            var d = await _documentService.GetQuoteByIdAsync(did, ct);
            if (d != null) derivedDocs.Add(d);
        }

        ViewData["RequestDoc"]  = requestDoc;
        ViewData["Lines"]       = lines;
        ViewData["DerivedDocs"] = (IReadOnlyCollection<DocumentDto>)derivedDocs;
        ViewData["RequestId"]   = requestId;

        return PartialView("~/Views/Purchase/FulfillModal.cshtml");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FulfillmentCenter — İhtiyaç karşılama merkezi (çoklu seçim + gelişmiş filtre)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Karşılama Merkezi ana sayfası.
    /// GET /Purchase/FulfillmentCenter
    /// Tüm ihtiyaç kayıtları + lokasyonlar sayfa yüklenerek JS'e aktarılır;
    /// seçim ve filtreleme tamamen client-side. Kalem detayları seçim değişince
    /// /Purchase/RequestLines AJAX ile çekilir.
    /// </summary>
    [HttpGet("/Purchase/FulfillmentCenter")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseFulfillment)]
    public async Task<IActionResult> FulfillmentCenter(CancellationToken ct)
    {
        var requests = await _documentService.GetByTypeAsync("alis_talebi", search: null, status: null, ct);
        var locations = await _logisticsService.GetLocationsAsync(ct);

        var contactIds = requests
            .Where(r => r.ContactId.HasValue).Select(r => r.ContactId!.Value).Distinct().ToList();
        var docIds = requests.Select(r => r.Id).ToList();

        // Cari grubu + malzeme grubu haritaları
        var contactGroupMap = contactIds.Count > 0
            ? await LoadFcContactGroupMapAsync(contactIds, ct)
            : new Dictionary<int, string>();
        var itemGroupMap = docIds.Count > 0
            ? await LoadFcItemGroupMapAsync(docIds, ct)
            : new Dictionary<int, List<string>>();
        var materialMap = docIds.Count > 0
            ? await LoadFcMaterialMapAsync(docIds, ct)
            : new Dictionary<int, List<object>>();

        // Widget şemaları (genel belge + kalem)
        var masterSchema = await _widgetService.GetFormSchemaByCodeAsync("PURCHASE_REQUEST_EDIT",  ct);
        var lineSchema   = await _widgetService.GetFormSchemaByCodeAsync("PURCHASE_REQUEST_LINES", ct);

        static bool IsFilterable(string dt) =>
            !string.Equals(dt, "group", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(dt, "grid",  StringComparison.OrdinalIgnoreCase);

        var masterWdefs = masterSchema?.Widgets
            .Where(w => w.IsActive && IsFilterable(w.DataType))
            .OrderBy(w => w.SortOrder).ToList() ?? [];
        var lineWdefs = lineSchema?.Widgets
            .Where(w => w.IsActive && IsFilterable(w.DataType))
            .OrderBy(w => w.SortOrder).ToList() ?? [];

        // Batch master widget değerleri (tek sorgu, N+1 yok)
        var recordIds   = docIds.Select(id => id.ToString()).ToArray();
        var batchMaster = masterWdefs.Count > 0 && recordIds.Length > 0
            ? await _widgetService.GetBatchRenderModelsAsync("PURCHASE_REQUEST_EDIT", recordIds, ct)
            : new Dictionary<string, IReadOnlyCollection<WidgetRenderDto>>();

        // Kalem widget değerleri (SQL, doc bazında aggregate)
        var lineWdgByDoc = lineWdefs.Count > 0 && docIds.Count > 0
            ? await LoadFcLineWidgetMapAsync(docIds, "PURCHASE_REQUEST_LINES", ct)
            : new Dictionary<int, Dictionary<string, List<string>>>();

        // JS'e gidecek widget tanımları: master + kalem, birleşik liste
        var fcJsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        var widgetDefsForJs = masterWdefs
            .Select(w => (object)new { id = w.WidgetCode, label = w.Label, dataType = w.DataType.ToLowerInvariant(), level = "master" })
            .Concat(lineWdefs
            .Select(w => (object)new { id = w.WidgetCode, label = w.Label, dataType = w.DataType.ToLowerInvariant(), level = "line" }))
            .ToList();

        // masterWidgetValues: { "docId" → { widgetCode → value } }
        var masterWValMap = batchMaster.ToDictionary(
            kv => kv.Key,
            kv => (object)kv.Value
                .Where(dto => dto.Value != null)
                .ToDictionary(dto => dto.WidgetId, dto => dto.Value));

        // lineWidgetValues:  { "docId" → { widgetCode → [val, ...] } }
        var lineWValMap = lineWdgByDoc.ToDictionary(
            kv => kv.Key.ToString(),
            kv => (object)kv.Value);

        // Ek saha kolonları (cbv_FulfillmentLineExtras view kolonları)
        var extraCols = await GetFulfillmentExtraColumnsAsync(ct);
        var extraColTuples = extraCols
            .Select(c => (
                Key  : System.Text.RegularExpressions.Regex.Replace(c, "[^a-zA-Z0-9]", "_").ToLowerInvariant(),
                Label: c
            ))
            .ToList();

        ViewData["Title"]           = "Karşılama Merkezi";
        ViewData["Requests"]        = (IReadOnlyCollection<DocumentListItemDto>)requests;
        ViewData["Locations"]       = (IReadOnlyCollection<LocationDto>)locations;
        ViewData["ContactGroupMap"] = contactGroupMap;
        ViewData["ItemGroupMap"]    = itemGroupMap;
        ViewData["WidgetDefsJson"]      = JsonSerializer.Serialize(widgetDefsForJs, fcJsonOpts);
        ViewData["MasterWidgetValJson"] = JsonSerializer.Serialize(masterWValMap,   fcJsonOpts);
        ViewData["LineWidgetValJson"]   = JsonSerializer.Serialize(lineWValMap,     fcJsonOpts);
        ViewData["MaterialMapJson"]     = JsonSerializer.Serialize(
            materialMap.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value), fcJsonOpts);
        ViewData["ExtraColumnTuples"] = extraColTuples;
        ViewData["ExtraColumnsJson"]  = JsonSerializer.Serialize(
            extraColTuples.Select(t => new { key = t.Key, label = t.Label }).ToList(), fcJsonOpts);

        var pendingApprovalDocIds = await GetPendingApprovalDocIdsAsync(ct);
        ViewData["PendingApprovalDocIdsJson"] = JsonSerializer.Serialize(pendingApprovalDocIds, fcJsonOpts);

        // İhtiyaç Kaydı onay tetikleme açık mı → karşılama ekranında seçim kapısını belirler.
        // Açık: yalnızca Onaylı belgeler seçilebilir. Kapalı: tümü seçilebilir.
        ViewData["ApprovalRequired"] = await _companyParams.GetStringAsync(
            ApprovalParameters.FormCode, ApprovalParameters.EnabledKey("PurchaseRequest"), ct) != "false";

        return View("~/Views/Purchase/FulfillmentCenter.cshtml");
    }

    /// <summary>ContactId → CariGroup.Name haritası (FulfillmentCenter filtresi için).</summary>
    private async Task<Dictionary<int, string>> LoadFcContactGroupMapAsync(
        IReadOnlyList<int> contactIds, CancellationToken ct)
    {
        var s         = _schema.Replace("]", "]]");
        var paramList = string.Join(",", contactIds.Select((_, i) => $"@c{i}"));
        var map       = new Dictionary<int, string>();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT ca.[Id], cg.[Name]
            FROM [{s}].[Contact] ca
            INNER JOIN [{s}].[CariGroup] cg ON cg.[Id] = ca.[ContactGroupId]
            WHERE ca.[Id] IN ({paramList}) AND cg.[IsActive] = 1;
            """;
        for (var i = 0; i < contactIds.Count; i++)
            cmd.Parameters.Add(new SqlParameter($"@c{i}", contactIds[i]));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            map[r.GetInt32(0)] = r.GetString(1);
        return map;
    }

    /// <summary>
    /// DocumentId → { widgetCode → [uniqueValues] } haritası.
    /// Kalem bazlı widget değerlerini belge düzeyinde toplar (FulfillmentCenter filtresi için).
    /// </summary>
    private async Task<Dictionary<int, Dictionary<string, List<string>>>> LoadFcLineWidgetMapAsync(
        IReadOnlyList<int> docIds, string lineFormCode, CancellationToken ct)
    {
        var s         = _schema.Replace("]", "]]");
        var paramList = string.Join(",", docIds.Select((_, i) => $"@d{i}"));
        var result    = new Dictionary<int, Dictionary<string, List<string>>>();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT dl.[DocumentId], wm.[WidgetCode], wt.[Value]
            FROM [{s}].[DocumentLine]  dl
            INNER JOIN [{s}].[Forms]     f   ON f.[FormCode]   = @FormCode
            INNER JOIN [{s}].[WidgetMas] wm  ON wm.[FormId]    = f.[Id]
                AND wm.[IsActive] = 1
                AND wm.[DataType] NOT IN (N'group', N'grid')
            INNER JOIN [{s}].[WidgetTra] wt  ON wt.[WidgetId]  = wm.[Id]
                AND wt.[RecordId] = CAST(dl.[Id] AS NVARCHAR(60))
            WHERE dl.[DocumentId] IN ({paramList})
              AND wt.[Value] IS NOT NULL
              AND wt.[Value] != N''
            ORDER BY dl.[DocumentId], wm.[WidgetCode];
            """;
        cmd.Parameters.Add(new SqlParameter("@FormCode", lineFormCode));
        for (var i = 0; i < docIds.Count; i++)
            cmd.Parameters.Add(new SqlParameter($"@d{i}", docIds[i]));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var docId = r.GetInt32(0);
            var code  = r.GetString(1);
            var value = r.IsDBNull(2) ? null : r.GetString(2);
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!result.ContainsKey(docId))             result[docId]        = new();
            if (!result[docId].ContainsKey(code))       result[docId][code]  = new();
            if (!result[docId][code].Contains(value))   result[docId][code].Add(value);
        }
        return result;
    }

    /// <summary>DocumentId → [MaterialGroup.GroupDescription, …] haritası (FulfillmentCenter filtresi için).</summary>
    private async Task<Dictionary<int, List<string>>> LoadFcItemGroupMapAsync(
        IReadOnlyList<int> docIds, CancellationToken ct)
    {
        var s         = _schema.Replace("]", "]]");
        var paramList = string.Join(",", docIds.Select((_, i) => $"@d{i}"));
        var map       = new Dictionary<int, List<string>>();

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT DISTINCT dl.[DocumentId], mg.[GroupDescription]
            FROM [{s}].[DocumentLine] dl
            INNER JOIN [{s}].[Items]               i   ON i.[Id]          = dl.[ItemId]
            INNER JOIN [{s}].[MaterialGroupMappings] mgm ON mgm.[ItemId]  = i.[Id]
            INNER JOIN [{s}].[MaterialGroups]       mg  ON mg.[GroupCode] = mgm.[GroupCode]
            WHERE dl.[DocumentId] IN ({paramList})
              AND mg.[GroupDescription] IS NOT NULL
              AND mg.[GroupDescription] != N'';
            """;
        for (var i = 0; i < docIds.Count; i++)
            cmd.Parameters.Add(new SqlParameter($"@d{i}", docIds[i]));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var docId = r.GetInt32(0);
            var grp   = r.GetString(1);
            if (!map.ContainsKey(docId)) map[docId] = new List<string>();
            if (!map[docId].Contains(grp)) map[docId].Add(grp);
        }
        return map;
    }

    /// <summary>DocumentId → [{code, name, hasStock}] haritası (FulfillmentCenter malzeme/stok filtresi için).</summary>
    private async Task<Dictionary<int, List<object>>> LoadFcMaterialMapAsync(
        IReadOnlyList<int> docIds, CancellationToken ct)
    {
        var s         = _schema.Replace("]", "]]");
        var paramList = string.Join(",", docIds.Select((_, i) => $"@d{i}"));
        var result    = new Dictionary<int, List<object>>();
        var (seFilter, seParams) = await BuildStockEffectFilterAsync("sd", ct);

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT dl.[DocumentId],
                   i.[Code]  AS MaterialCode,
                   i.[Name]  AS MaterialName,
                   CASE WHEN COALESCE(sm.[Balance], 0) > 0 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasStock
            FROM [{s}].[DocumentLine] dl
            INNER JOIN [{s}].[Items] i ON i.[Id] = dl.[ItemId]
            LEFT JOIN (
                SELECT sdl.[ItemId],
                       SUM(CASE
                           WHEN sdl.[MovementType] IN (2,3) AND sdl.[LocationId]     IS NOT NULL THEN  sdl.[BaseQuantity]
                           WHEN sdl.[MovementType] IN (1,3) AND sdl.[FromLocationId] IS NOT NULL THEN -sdl.[BaseQuantity]
                           WHEN sdl.[MovementType] = 4 AND sdl.[LocationId]     IS NOT NULL THEN  sdl.[BaseQuantity]
                           WHEN sdl.[MovementType] = 4 AND sdl.[FromLocationId] IS NOT NULL THEN -sdl.[BaseQuantity]
                           ELSE 0
                       END) AS [Balance]
                FROM [{s}].[DocumentLine] sdl
                INNER JOIN [{s}].[Document] sd ON sd.[id] = sdl.[DocumentId]
                WHERE sdl.[MovementType] IS NOT NULL AND sd.[IsActive] = 1{seFilter}
                GROUP BY sdl.[ItemId]
            ) sm ON sm.[ItemId] = dl.[ItemId]
            WHERE dl.[DocumentId] IN ({paramList})
              AND dl.[ItemId] IS NOT NULL;
            """;
        for (var i = 0; i < docIds.Count; i++)
            cmd.Parameters.Add(new SqlParameter($"@d{i}", docIds[i]));
        foreach (var p in seParams) cmd.Parameters.Add(p);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var docId    = r.GetInt32(0);
            var matCode  = r.IsDBNull(1) ? null : r.GetString(1);
            var matName  = r.IsDBNull(2) ? null : r.GetString(2);
            var hasStock = !r.IsDBNull(3) && r.GetBoolean(3);

            if (!result.TryGetValue(docId, out var list))
                result[docId] = list = [];
            list.Add(new { code = matCode, name = matName, hasStock });
        }
        return result;
    }

    /// <summary>
    /// Çoklu ihtiyaç kaydının kalemlerini tek JSON dizisi olarak döner.
    /// GET /Purchase/RequestLines?requestIds=1&amp;requestIds=2&amp;...
    /// Yanıt: [{ requestId, requestNumber, lineId, itemId, materialName, ... }]
    /// </summary>
    [HttpGet("/Purchase/RequestLines")]
    public async Task<IActionResult> RequestLines([FromQuery] int[] requestIds, CancellationToken ct)
    {
        if (requestIds == null || requestIds.Length == 0)
            return Json(Array.Empty<object>());

        try
        {
            // Kalem verisi
            var lineData = new List<(int rid, string reqNum, CalibraHub.Application.Contracts.DocumentLineDto l)>();
            foreach (var rid in requestIds.Distinct())
            {
                var doc   = await _documentService.GetQuoteByIdAsync(rid, ct);
                var lines = await _documentService.GetQuoteLinesAsync(rid, ct);
                if (doc == null) continue;
                foreach (var l in lines)
                    lineData.Add((rid, doc.DocumentNumber ?? "", l));
            }

            // Ek saha verisi (cbv_FulfillmentLineExtras — yalnızca view'da kolon varsa)
            var extrasMap = new Dictionary<int, Dictionary<string, string?>>();
            var extraCols = await GetFulfillmentExtraColumnsAsync(ct);
            if (extraCols.Count > 0 && lineData.Count > 0)
            {
                var s2 = _schema.Replace("]", "]]");
                var distinctDocIds = requestIds.Distinct().ToArray();
                var paramList = string.Join(",", distinctDocIds.Select((_, i) => $"@d{i}"));
                await using var conn2 = await _connectionFactory.OpenConnectionAsync(ct);
                await using var cmd2  = conn2.CreateCommand();
                cmd2.CommandText = $"SELECT * FROM [{s2}].[cbv_FulfillmentLineExtras] WHERE [DocumentId] IN ({paramList});";
                for (var i = 0; i < distinctDocIds.Length; i++)
                    cmd2.Parameters.Add(new SqlParameter($"@d{i}", distinctDocIds[i]));
                await using var r2 = await cmd2.ExecuteReaderAsync(ct);
                while (await r2.ReadAsync(ct))
                {
                    var lineId = r2.GetInt32(r2.GetOrdinal("LineId"));
                    var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    for (var ci = 0; ci < r2.FieldCount; ci++)
                    {
                        var cname = r2.GetName(ci);
                        if (cname is "DocumentId" or "LineId") continue;
                        dict[cname] = r2.IsDBNull(ci) ? null : r2.GetValue(ci)?.ToString();
                    }
                    extrasMap[lineId] = dict;
                }
            }

            var result = lineData.Select(t => (object)new
            {
                requestId           = t.rid,
                requestNumber       = t.reqNum,
                lineId              = t.l.Id,
                itemId              = t.l.ItemId,
                materialCode        = t.l.MaterialCode,
                materialName        = t.l.MaterialName,
                unitId              = t.l.UnitId,
                unitCode            = t.l.UnitCode ?? t.l.UnitName,
                quantity            = t.l.Quantity,
                locationId          = t.l.LocationId,
                locationName        = t.l.LocationName,
                combinationId       = t.l.CombinationId,
                notes               = t.l.Notes,
                fulfilledFromStock  = t.l.FulfilledFromStock,
                fulfilledByPurchase = t.l.FulfilledByPurchase,
                fulfillmentStatus   = t.l.FulfillmentStatus,
                remaining           = t.l.Quantity - t.l.FulfilledFromStock - t.l.FulfilledByPurchase,
                extras              = extrasMap.TryGetValue(t.l.Id, out var ex) ? ex : null,
            }).ToList();

            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { error = true, message = "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>
    /// Stok bakiyelerini döner — FulfillmentCenter'ın kalem tablosu için AJAX endpoint.
    /// GET /Purchase/StockBalances?itemIds=1&amp;itemIds=2&amp;...
    /// Yanıt: [{ itemId, locationId, locationName, balance }]
    /// MovementType: 1=Çıkış (Issue), 2=Giriş (Receipt)
    /// </summary>
    [HttpGet("/Purchase/StockBalances")]
    public async Task<IActionResult> StockBalances([FromQuery] int[] itemIds, CancellationToken ct)
    {
        if (itemIds == null || itemIds.Length == 0)
            return Json(Array.Empty<object>());

        var s          = _schema.Replace("]", "]]");
        var paramList  = string.Join(",", itemIds.Select((_, i) => $"@i{i}"));
        var companyId  = _connectionFactory.ResolveCurrentCompanyId();
        var (seFilter, seParams) = await BuildStockEffectFilterAsync("d", ct);

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        // 2026-07-02: Tek kaynak — DocumentLine (MovementType: 1=Issue/2=Receipt/3=Transfer/4=Adjust).
        // MovementType IS NULL = ticari/henuz kesinlesmemis satir (Transfer/Giris/Cikis Draft'ta
        // duzenlenirken MovementType bos kalir — SaveLinesAsync ile serbestce degistirilebilir;
        // "Kesinlestir" aninda tek UPDATE ile set edilir). WorkOrder/Sayim append-only yazdigi anda
        // zaten MovementType dolu gelir — ayrica Document.status kontrolüne gerek yok (companion
        // pattern'de Document.Status sabit Draft kalabilir, otorite companion'in kendi status'undedir
        // — bkz. ArgeProjectService).
        // STOCK_EFFECT_{code}=false olan belge türleri bakiye dışı bırakılır (seFilter).
        cmd.CommandText = $"""
            WITH Combined AS (
                -- Receipt: hedef lokasyona +miktar (ana birim)
                SELECT dl.ItemId, dl.LocationId, dl.BaseQuantity AS Bal
                FROM [{s}].[DocumentLine] dl
                JOIN [{s}].[Document] d ON d.id = dl.DocumentId
                WHERE dl.ItemId IN ({paramList}) AND dl.MovementType = 2
                  AND d.CompanyId = @CompanyId AND d.IsActive = 1{seFilter}

                UNION ALL

                -- Issue: kaynak lokasyondan -miktar (ana birim)
                SELECT dl.ItemId, dl.FromLocationId, -dl.BaseQuantity
                FROM [{s}].[DocumentLine] dl
                JOIN [{s}].[Document] d ON d.id = dl.DocumentId
                WHERE dl.ItemId IN ({paramList}) AND dl.MovementType = 1
                  AND d.CompanyId = @CompanyId AND d.IsActive = 1{seFilter}

                UNION ALL

                -- Transfer: hedef +miktar (ana birim)
                SELECT dl.ItemId, dl.LocationId, dl.BaseQuantity
                FROM [{s}].[DocumentLine] dl
                JOIN [{s}].[Document] d ON d.id = dl.DocumentId
                WHERE dl.ItemId IN ({paramList}) AND dl.MovementType = 3 AND dl.LocationId IS NOT NULL
                  AND d.CompanyId = @CompanyId AND d.IsActive = 1{seFilter}

                UNION ALL

                -- Transfer: kaynak -miktar (ana birim)
                SELECT dl.ItemId, dl.FromLocationId, -dl.BaseQuantity
                FROM [{s}].[DocumentLine] dl
                JOIN [{s}].[Document] d ON d.id = dl.DocumentId
                WHERE dl.ItemId IN ({paramList}) AND dl.MovementType = 3 AND dl.FromLocationId IS NOT NULL
                  AND d.CompanyId = @CompanyId AND d.IsActive = 1{seFilter}

                UNION ALL

                -- Adjust (Sayim farki): LocationId doluysa +miktar (fazla cikti, ana birim)
                SELECT dl.ItemId, dl.LocationId, dl.BaseQuantity
                FROM [{s}].[DocumentLine] dl
                JOIN [{s}].[Document] d ON d.id = dl.DocumentId
                WHERE dl.ItemId IN ({paramList}) AND dl.MovementType = 4 AND dl.LocationId IS NOT NULL
                  AND d.CompanyId = @CompanyId AND d.IsActive = 1{seFilter}

                UNION ALL

                -- Adjust (Sayim farki): FromLocationId doluysa -miktar (eksik cikti, ana birim)
                SELECT dl.ItemId, dl.FromLocationId, -dl.BaseQuantity
                FROM [{s}].[DocumentLine] dl
                JOIN [{s}].[Document] d ON d.id = dl.DocumentId
                WHERE dl.ItemId IN ({paramList}) AND dl.MovementType = 4 AND dl.FromLocationId IS NOT NULL
                  AND d.CompanyId = @CompanyId AND d.IsActive = 1{seFilter}
            )
            SELECT c.ItemId, c.LocationId, loc.LocationName, SUM(c.Bal) AS Balance
            FROM Combined c
            LEFT JOIN [{s}].[Location] loc ON loc.Id = c.LocationId
            GROUP BY c.ItemId, c.LocationId, loc.LocationName
            HAVING SUM(c.Bal) > 0
            ORDER BY c.ItemId, SUM(c.Bal) DESC;
            """;
        for (var i = 0; i < itemIds.Length; i++)
            cmd.Parameters.Add(new SqlParameter($"@i{i}", itemIds[i]));
        cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        foreach (var p in seParams) cmd.Parameters.Add(p);

        var result = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new
            {
                itemId       = r.GetInt32(0),
                locationId   = r.IsDBNull(1) ? (int?)null : r.GetInt32(1),
                locationName = r.IsDBNull(2) ? null : r.GetString(2),
                balance      = r.GetDecimal(3),
            });
        }
        return Json(result);
    }

    /// <summary>
    /// Depo transferi oluşturur ve kaynak İhtiyaç kaydına RefNo üzerinden bağlar.
    /// POST /Purchase/CreateTransfer
    /// Yanıt: { ok: true, docNo } veya { ok: false, error }
    /// </summary>
    [HttpPost("/Purchase/CreateTransfer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTransfer([FromBody] CreateTransferRequest req, CancellationToken ct)
    {
        try
        {
            if (req?.Lines == null || req.Lines.Count == 0)
                return Json(new { ok = false, error = "Kalem girilmedi." });

            var validLines = req.Lines
                .Where(l => l.Qty > 0 && l.FromLocationId > 0)
                .ToList();

            if (validLines.Count == 0)
                return Json(new { ok = false, error = "Geçerli transfer kalemi bulunamadı (miktar > 0 ve kaynak depo zorunlu)." });

            if (req.RequestIds?.Count > 0)
            {
                var guardError = await CheckFulfillmentApprovalGuardAsync(req.RequestIds, ct);
                if (guardError != null) return Json(new { ok = false, error = guardError });
            }

            // RefNo: kaynak İhtiyaç belge numaraları — izlenebilirlik (birden fazla olabilir).
            string? refNo = null;
            if (req.RequestIds?.Count > 0)
            {
                var nums = new List<string>();
                foreach (var rid in req.RequestIds)
                {
                    var srcDoc = await _documentService.GetQuoteByIdAsync(rid, ct);
                    if (srcDoc != null) nums.Add(srcDoc.DocumentNumber);
                }
                if (nums.Count > 0) refNo = string.Join(", ", nums);
            }

            var saveReq = new SaveStockDocRequest(
                Id:             null,
                DocType:        "TRANSFER",
                DocNo:          null,
                DocDate:        DateTime.Today,
                FromLocationId: null,  // satır bazlı farklı lokasyonlar
                ToLocationId:   null,  // satır bazlı farklı lokasyonlar
                RefNo:          refNo,
                Notes:          req.Notes,
                Lines:          validLines.Select(l => new SaveStockDocLineRequest(
                    Id:             null,
                    ItemId:         l.ItemId,
                    MaterialCode:   null,
                    MaterialName:   null,
                    UnitId:         l.UnitId,
                    Qty:            l.Qty,
                    CombinationId:  l.CombinationId,
                    Notes:          l.Notes,
                    FromLocationId: l.FromLocationId,
                    ToLocationId:   l.ToLocationId,
                    UnitCost:       null
                )).ToList(),
                ArgeProjectId:  null
            );

            var (newDocId, docNo) = await _stockDocRepo.SaveAsync(saveReq, CurrentUserId(), ct);

            // Belge soyağacı: transfer fişi ← kaynak İhtiyaç belge(ler)i (soyağacı akış görünümü)
            await LinkFulfillmentSourcesAsync(newDocId, req.RequestIds, ct);

            // Fulfillment takibi: RequestLineId gönderilmiş satırların FulfilledFromStock artır
            var linesWithTracking = validLines
                .Where(l => l.RequestLineId.HasValue && l.RequestLineId.Value > 0)
                .GroupBy(l => l.RequestLineId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Qty));

            if (linesWithTracking.Count > 0 && req.RequestIds?.Count > 0)
            {
                // Mevcut satır değerlerini request belgelerinden çek (N+1 ama küçük set)
                var allLines = new Dictionary<int, DocumentLineDto>();
                foreach (var rid in req.RequestIds)
                {
                    var lines2 = await _documentService.GetQuoteLinesAsync(rid, ct);
                    foreach (var l in lines2) allLines[l.Id] = l;
                }

                foreach (var (reqLineId, transferQty) in linesWithTracking)
                {
                    if (!allLines.TryGetValue(reqLineId, out var ln)) continue;
                    var newFromStock = ln.FulfilledFromStock + transferQty;
                    await _documentRepo.UpdateLineFulfillmentAsync(
                        reqLineId, newFromStock, ln.FulfilledByPurchase, ct);
                }
            }

            return Json(new { ok = true, docNo });
        }
        catch (CalibraHub.Domain.Exceptions.NegativeBalanceException nbex)
        {
            return Json(new { ok = false, error = nbex.Message });
        }
        catch (InvalidOperationException ioex)
        {
            // Lot zorunluluğu / lot bakiyesi doğrulama mesajları kullanıcıya aynen gösterilir.
            return Json(new { ok = false, error = ioex.Message });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>
    /// Ambar çıkış fişi oluşturur (STOCK_OUT) ve ihtiyaç satırlarının FulfilledFromStock günceller.
    /// POST /Purchase/CreateStockIssue
    /// </summary>
    [HttpPost("/Purchase/CreateStockIssue")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateStockIssue([FromBody] CreateStockIssueRequest req, CancellationToken ct)
    {
        try
        {
            if (req?.Lines == null || req.Lines.Count == 0)
                return Json(new { ok = false, error = "Kalem girilmedi." });

            var validLines = req.Lines
                .Where(l => l.Qty > 0 && l.FromLocationId > 0)
                .ToList();

            if (validLines.Count == 0)
                return Json(new { ok = false, error = "Geçerli çıkış kalemi bulunamadı (miktar > 0 ve depo zorunlu)." });

            if (req.RequestIds?.Count > 0)
            {
                var guardError = await CheckFulfillmentApprovalGuardAsync(req.RequestIds, ct);
                if (guardError != null) return Json(new { ok = false, error = guardError });
            }

            string? refNo = null;
            if (req.RequestIds?.Count > 0)
            {
                var nums = new List<string>();
                foreach (var rid in req.RequestIds)
                {
                    var srcDoc = await _documentService.GetQuoteByIdAsync(rid, ct);
                    if (srcDoc != null) nums.Add(srcDoc.DocumentNumber);
                }
                if (nums.Count > 0) refNo = string.Join(", ", nums);
            }

            var saveReq = new SaveStockDocRequest(
                Id:             null,
                DocType:        "STOCK_OUT",
                DocNo:          null,
                DocDate:        DateTime.Today,
                FromLocationId: null,
                ToLocationId:   null,
                RefNo:          refNo,
                Notes:          req.Notes,
                Lines:          validLines.Select(l => new SaveStockDocLineRequest(
                    Id:             null,
                    ItemId:         l.ItemId,
                    MaterialCode:   null,
                    MaterialName:   null,
                    UnitId:         l.UnitId,
                    Qty:            l.Qty,
                    CombinationId:  l.CombinationId,
                    Notes:          l.Notes,
                    FromLocationId: l.FromLocationId,
                    ToLocationId:   null,
                    UnitCost:       null
                )).ToList(),
                ArgeProjectId:  null
            );

            var (newDocId, docNo) = await _stockDocRepo.SaveAsync(saveReq, CurrentUserId(), ct);

            // Belge soyağacı: ambar çıkış fişi ← kaynak İhtiyaç belge(ler)i
            await LinkFulfillmentSourcesAsync(newDocId, req.RequestIds, ct);

            // Fulfillment takibi: FulfilledFromStock artır
            var linesWithTracking = validLines
                .Where(l => l.RequestLineId.HasValue && l.RequestLineId.Value > 0)
                .GroupBy(l => l.RequestLineId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Qty));

            if (linesWithTracking.Count > 0 && req.RequestIds?.Count > 0)
            {
                var allLines = new Dictionary<int, CalibraHub.Application.Contracts.DocumentLineDto>();
                foreach (var rid in req.RequestIds)
                {
                    var lines2 = await _documentService.GetQuoteLinesAsync(rid, ct);
                    foreach (var l in lines2) allLines[l.Id] = l;
                }
                foreach (var (reqLineId, issueQty) in linesWithTracking)
                {
                    if (!allLines.TryGetValue(reqLineId, out var ln)) continue;
                    var newFromStock = ln.FulfilledFromStock + issueQty;
                    await _documentRepo.UpdateLineFulfillmentAsync(reqLineId, newFromStock, ln.FulfilledByPurchase, ct);
                }
            }

            return Json(new { ok = true, docNo });
        }
        catch (CalibraHub.Domain.Exceptions.NegativeBalanceException nbex)
        {
            return Json(new { ok = false, error = nbex.Message });
        }
        catch (InvalidOperationException ioex)
        {
            // Lot zorunluluğu / lot bakiyesi doğrulama mesajları kullanıcıya aynen gösterilir.
            return Json(new { ok = false, error = ioex.Message });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ── Depodan Karşıla ──────────────────────────────────────────────────────

    /// <summary>
    /// Mevcut şirket parametrelerinden Depodan Karşıla konfigürasyonunu döner.
    /// GET /Purchase/FulfillmentLocationConfig
    /// </summary>
    [HttpGet("/Purchase/FulfillmentLocationConfig")]
    public async Task<IActionResult> FulfillmentLocationConfig(CancellationToken ct)
    {
        const string fc = "PURCHASE_FULFILLMENT";
        var mode    = await _companyParams.GetStringAsync(fc, "FULFILLMENT_LOCATION_MODE", ct) ?? "SPECIFIC";
        var idsRaw  = await _companyParams.GetStringAsync(fc, "FULFILLMENT_LOCATION_IDS",  ct) ?? "";
        var ids     = idsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim()).Where(s => int.TryParse(s, out _))
                            .Select(int.Parse).ToList();
        return Json(new { mode, locationIds = ids });
    }

    /// <summary>
    /// Seçili ihtiyaç kalemlerini FIFO stok dağıtımıyla otomatik olarak depoden karşılar.
    /// POST /Purchase/FulfillFromStock
    /// Mantık:
    ///   1. �?irket parametresinden karşılama deposu/modunu oku.
    ///   2. Seçili satırların kalan miktarlarını çek.
    ///   3. Belge tarihine göre FIFO sırala (eski talep önce karşılanır).
    ///   4. Her kalem için uygun depolardan stok al, bakiyeyi düş.
    ///   5. CreateStockIssue iç mantığıyla ambar çıkış fişi oluştur + FulfilledFromStock güncelle.
    /// </summary>
    [HttpPost("/Purchase/FulfillFromStock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FulfillFromStock([FromBody] FulfillFromStockRequest req, CancellationToken ct)
    {
        if (req?.LineIds == null || req.LineIds.Count == 0)
            return Json(new { ok = false, error = "Kalem seçilmedi." });

        const string fc = "PURCHASE_FULFILLMENT";
        var mode   = await _companyParams.GetStringAsync(fc, "FULFILLMENT_LOCATION_MODE", ct) ?? "SPECIFIC";
        var idsRaw = await _companyParams.GetStringAsync(fc, "FULFILLMENT_LOCATION_IDS",  ct) ?? "";

        List<int>? configuredLocIds = null;
        if (string.Equals(mode, "SPECIFIC", StringComparison.OrdinalIgnoreCase))
        {
            configuredLocIds = idsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(s => s.Trim()).Where(s => int.TryParse(s, out _))
                                     .Select(int.Parse).ToList();
            if (configuredLocIds.Count == 0)
                return Json(new { ok = false, error = "Karşılama deposu tanımlanmamış. �?irket Ayarları → Satın Alma bölümünden depo seçin." });
        }

        var s         = _schema.Replace("]", "]]");
        var paramList = string.Join(",", req.LineIds.Select((_, i) => $"@l{i}"));

        // -- Seçili satırları yükle --
        var lines = new List<(int LineId, int DocId, int ItemId, int? UnitId, decimal Remaining, decimal FromStock, decimal FromPurch, DateTime DocDate)>();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT dl.[Id], dl.[DocumentId], dl.[ItemId], dl.[UnitId],
                       dl.[Quantity], ISNULL(dl.[FulfilledFromStock],0), ISNULL(dl.[FulfilledByPurchase],0),
                       ISNULL(d.[DocumentDate], SYSUTCDATETIME())
                FROM [{s}].[DocumentLine] dl
                INNER JOIN [{s}].[Document] d ON d.[Id] = dl.[DocumentId]
                WHERE dl.[Id] IN ({paramList});
                """;
            for (var i = 0; i < req.LineIds.Count; i++)
                cmd.Parameters.Add(new SqlParameter($"@l{i}", req.LineIds[i]));

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var qty       = r.GetDecimal(4);
                var fromStk   = r.GetDecimal(5);
                var fromPur   = r.GetDecimal(6);
                var remaining = Math.Max(0, qty - fromStk - fromPur);
                lines.Add((r.GetInt32(0), r.GetInt32(1), r.GetInt32(2),
                           r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                           remaining, fromStk, fromPur, r.GetDateTime(7)));
            }
        }

        if (lines.Count == 0)
            return Json(new { ok = false, error = "Seçili kalemler bulunamadı." });

        if (lines.All(l => l.Remaining <= 0))
            return Json(new { ok = false, error = "Seçili kalemlerin tamamı zaten karşılanmış." });

        var guardError = await CheckFulfillmentApprovalGuardAsync(lines.Select(l => l.DocId), ct);
        if (guardError != null)
            return Json(new { ok = false, error = guardError });

        // -- Stok bakiyelerini çek --
        var distinctItemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var iParamList      = string.Join(",", distinctItemIds.Select((_, i) => $"@i{i}"));
        var (seFilter, seParams) = await BuildStockEffectFilterAsync("smd", ct);

        // ITEM_DEFAULT modunda her malzemenin varsayılan deposunu al
        var itemDefaultLoc = new Dictionary<int, int>(); // itemId → locationId
        if (!string.Equals(mode, "SPECIFIC", StringComparison.OrdinalIgnoreCase))
        {
            await using var cmdDef = conn.CreateCommand();
            cmdDef.CommandText = $"""
                SELECT [ItemId], [LocationId]
                FROM [{s}].[ItemLocation]
                WHERE [ItemId] IN ({iParamList}) AND [IsDefault] = 1;
                """;
            for (var i = 0; i < distinctItemIds.Count; i++)
                cmdDef.Parameters.Add(new SqlParameter($"@i{i}", distinctItemIds[i]));
            await using var rDef = await cmdDef.ExecuteReaderAsync(ct);
            while (await rDef.ReadAsync(ct))
                itemDefaultLoc[rDef.GetInt32(0)] = rDef.GetInt32(1);
        }

        // Stok sorgusu — lokasyon filtrelemesi moduna göre yapılır
        var stockByItemLoc = new Dictionary<int, Dictionary<int, decimal>>(); // itemId → (locId → balance)
        string locFilter = "";
        if (string.Equals(mode, "SPECIFIC", StringComparison.OrdinalIgnoreCase) && configuredLocIds != null)
        {
            var lParamList = string.Join(",", configuredLocIds.Select((_, i) => $"@loc{i}"));
            locFilter = $"AND c.[LocId] IN ({lParamList})";
        }

        await using (var cmd2 = conn.CreateCommand())
        {
            cmd2.CommandText = $"""
                WITH Combined AS (
                    SELECT sm.[ItemId], sm.[LocationId] AS [LocId], sm.[Quantity] AS [Bal]
                    FROM [{s}].[DocumentLine] sm
                    INNER JOIN [{s}].[Document] smd ON smd.[id] = sm.[DocumentId]
                    WHERE sm.[ItemId] IN ({iParamList}) AND smd.[IsActive] = 1
                      AND (sm.[MovementType] = 2 OR (sm.[MovementType] IN (3,4) AND sm.[LocationId] IS NOT NULL)){seFilter}

                    UNION ALL

                    SELECT sm.[ItemId], sm.[FromLocationId] AS [LocId], -sm.[Quantity]
                    FROM [{s}].[DocumentLine] sm
                    INNER JOIN [{s}].[Document] smd ON smd.[id] = sm.[DocumentId]
                    WHERE sm.[ItemId] IN ({iParamList}) AND smd.[IsActive] = 1
                      AND (sm.[MovementType] = 1 OR (sm.[MovementType] IN (3,4) AND sm.[FromLocationId] IS NOT NULL)){seFilter}
                )
                SELECT c.[ItemId], c.[LocId] AS [LocationId], SUM(c.[Bal]) AS [Balance]
                FROM Combined c
                WHERE 1=1 {locFilter}
                GROUP BY c.[ItemId], c.[LocId]
                HAVING SUM(c.[Bal]) > 0;
                """;
            for (var i = 0; i < distinctItemIds.Count; i++)
                cmd2.Parameters.Add(new SqlParameter($"@i{i}", distinctItemIds[i]));
            if (string.Equals(mode, "SPECIFIC", StringComparison.OrdinalIgnoreCase) && configuredLocIds != null)
                for (var i = 0; i < configuredLocIds.Count; i++)
                    cmd2.Parameters.Add(new SqlParameter($"@loc{i}", configuredLocIds[i]));
            foreach (var p in seParams) cmd2.Parameters.Add(p);

            await using var r2 = await cmd2.ExecuteReaderAsync(ct);
            while (await r2.ReadAsync(ct))
            {
                var itemId  = r2.GetInt32(0);
                var locId   = r2.IsDBNull(1) ? 0 : r2.GetInt32(1);
                var balance = r2.GetDecimal(2);
                if (!stockByItemLoc.ContainsKey(itemId)) stockByItemLoc[itemId] = new();
                stockByItemLoc[itemId][locId] = balance;
            }
        }

        // ITEM_DEFAULT: sadece varsayılan depoyu bırak
        if (!string.Equals(mode, "SPECIFIC", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var itemId in distinctItemIds)
            {
                if (!stockByItemLoc.ContainsKey(itemId)) continue;
                if (itemDefaultLoc.TryGetValue(itemId, out var defLoc))
                {
                    var bal = stockByItemLoc[itemId].GetValueOrDefault(defLoc);
                    stockByItemLoc[itemId] = bal > 0 ? new() { [defLoc] = bal } : new();
                }
                else
                    stockByItemLoc[itemId] = new();
            }
        }

        // -- FIFO dağıtım --
        // mutableStock: değiştirilebilir kopya
        var mutableStock = stockByItemLoc.ToDictionary(
            kv => kv.Key,
            kv => new Dictionary<int, decimal>(kv.Value));

        // Satırları belge tarihine göre sırala (eski talep önce)
        var sorted  = lines.OrderBy(l => l.DocDate).ThenBy(l => l.LineId).ToList();
        var planned = new List<StockIssueLineRequest>();
        var results = new List<object>();

        foreach (var line in sorted)
        {
            if (line.Remaining <= 0)
            {
                results.Add(new { lineId = line.LineId, fulfilled = 0m, note = "Zaten karşılanmış" });
                continue;
            }
            if (!mutableStock.TryGetValue(line.ItemId, out var locMap) || locMap.Values.All(b => b <= 0))
            {
                results.Add(new { lineId = line.LineId, fulfilled = 0m, note = "Stok yok" });
                continue;
            }

            decimal toFulfill = line.Remaining;
            decimal fulfilled = 0;
            // Yüksek bakiyeli depodan başla
            foreach (var locId in locMap.Keys.OrderByDescending(k => locMap[k]).ToList())
            {
                if (toFulfill <= 0) break;
                var avail = locMap[locId];
                if (avail <= 0) continue;
                var take = Math.Min(toFulfill, avail);
                planned.Add(new StockIssueLineRequest(
                    ItemId:         line.ItemId,
                    UnitId:         line.UnitId,
                    Qty:            take,
                    FromLocationId: locId,
                    CombinationId:  null,
                    Notes:          null,
                    RequestLineId:  line.LineId));
                mutableStock[line.ItemId][locId] -= take;
                fulfilled += take;
                toFulfill -= take;
            }

            var note = fulfilled >= line.Remaining
                ? "Tamamen karşılandı"
                : $"Kısmen ({fulfilled:F2}/{line.Remaining:F2})";
            results.Add(new { lineId = line.LineId, fulfilled, note });
        }

        if (planned.Count == 0)
            return Json(new { ok = false, error = "Yeterli stok bulunamadı — hiçbir kalem karşılanamadı." });

        // -- Stok fişi oluştur ve FulfilledFromStock güncelle --
        var docIds = lines.Select(l => l.DocId).Distinct().ToList();
        string? refNo = null;
        foreach (var docId in docIds)
        {
            var doc = await _documentService.GetQuoteByIdAsync(docId, ct);
            if (doc != null) refNo = (refNo == null ? "" : refNo + ", ") + doc.DocumentNumber;
        }

        var saveReq = new SaveStockDocRequest(
            Id:             null,
            DocType:        "STOCK_OUT",
            DocNo:          null,
            DocDate:        DateTime.Today,
            FromLocationId: null,
            ToLocationId:   null,
            RefNo:          refNo,
            Notes:          req.Notes,
            Lines:          planned.Select(l => new SaveStockDocLineRequest(
                                Id:             null,
                                ItemId:         l.ItemId,
                                MaterialCode:   null,
                                MaterialName:   null,
                                UnitId:         l.UnitId,
                                Qty:            l.Qty,
                                CombinationId:  l.CombinationId,
                                Notes:          l.Notes,
                                FromLocationId: l.FromLocationId,
                                ToLocationId:   null,
                                UnitCost:       null)).ToList(),
            ArgeProjectId:  null);

        var (newDocId, docNo) = await _stockDocRepo.SaveAsync(saveReq, CurrentUserId(), ct);

        // Belge soyağacı: FIFO ambar çıkış fişi ← kaynak İhtiyaç belge(ler)i
        await LinkFulfillmentSourcesAsync(newDocId, docIds, ct);

        // FulfilledFromStock güncelle
        var lineMap = lines.ToDictionary(l => l.LineId);
        var byLineId = planned.GroupBy(l => l.RequestLineId!.Value)
                              .ToDictionary(g => g.Key, g => g.Sum(l => l.Qty));
        foreach (var (lineId, qty) in byLineId)
        {
            if (!lineMap.TryGetValue(lineId, out var ln)) continue;
            await _documentRepo.UpdateLineFulfillmentAsync(lineId, ln.FromStock + qty, ln.FromPurch, ct);
        }

        return Json(new { ok = true, docNo, results });
    }

    /// <summary>
    /// Satın alma talebi oluşturur (alis_siparisi belgesi) ve ihtiyaç satırlarının FulfilledByPurchase günceller.
    /// POST /Purchase/CreatePurchaseOrderFromIhtiyac
    /// </summary>
    [HttpPost("/Purchase/CreatePurchaseOrderFromIhtiyac")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePurchaseOrderFromIhtiyac(
        [FromBody] CreatePurchaseOrderFromIhtiyacRequest req, CancellationToken ct)
    {
        try
        {
            if (req?.Lines == null || req.Lines.Count == 0)
                return Json(new { ok = false, error = "Kalem girilmedi." });

            var validLines = req.Lines.Where(l => l.Qty > 0).ToList();
            if (validLines.Count == 0)
                return Json(new { ok = false, error = "Geçerli satın alma kalemi bulunamadı." });

            if (req.RequestIds?.Count > 0)
            {
                var guardError = await CheckFulfillmentApprovalGuardAsync(req.RequestIds, ct);
                if (guardError != null) return Json(new { ok = false, error = guardError });
            }

            var docType = await _documentTypeRepo.GetByCodeAsync("alis_siparisi", ct);
            if (docType == null)
                return Json(new { ok = false, error = "alis_siparisi belge tipi bulunamadı." });

            // Kaynak belge numaralarını refNo olarak ekle
            string? notes = req.Notes;
            if (req.RequestIds?.Count > 0)
            {
                var nums = new List<string>();
                foreach (var rid in req.RequestIds)
                {
                    var srcDoc = await _documentService.GetQuoteByIdAsync(rid, ct);
                    if (srcDoc != null) nums.Add(srcDoc.DocumentNumber);
                }
                if (nums.Count > 0)
                {
                    var refLine = "Kaynak: " + string.Join(", ", nums);
                    notes = string.IsNullOrWhiteSpace(notes) ? refLine : refLine + "\n" + notes;
                }
            }

            var saveDocReq = new CalibraHub.Application.Contracts.SaveDocumentRequest(
                Id:                    null,
                DocumentDate:          DateTime.Today,
                ValidUntil:            null,
                ContactId:             req.ContactId,
                ContactName:           null,
                ContactAddress:        null,
                SalesRepId:            null,
                CurrencyId:            1,   // varsayılan TL
                DiscountRate:          0m,
                TaxRate:               0m,
                PaymentTerms:          null,
                DeliveryTerms:         null,
                DeliveryAddress:       null,
                Notes:                 notes,
                Lines:                 validLines.Select(l => new CalibraHub.Application.Contracts.SaveDocumentLineRequest(
                    Id:                 null,
                    ItemId:             l.ItemId,
                    UnitId:             l.UnitId,
                    Quantity:           l.Qty,
                    UnitPrice:          0m,
                    DiscountRate:       0m,
                    CombinationId:      l.CombinationId,
                    LocationId:         null,
                    Notes:              l.Notes
                )).ToList(),
                DocumentTypeId:        docType.Id,
                FromRequestId:         req.RequestIds?.FirstOrDefault()
            );

            var (success, error, doc, _) = await _documentService.SaveQuoteAsync(saveDocReq, CurrentUserId(), User?.Identity?.Name, ct);
            if (!success || doc == null)
                return Json(new { ok = false, error = error ?? "Belge oluşturulamadı." });

            // Belge soyağacı: sipariş ← TÜM kaynak İhtiyaç belgeleri (SaveQuoteAsync yalnız
            // FromRequestId=ilkini bağlar; çoklu kaynak için hepsini idempotent ekle).
            await LinkFulfillmentSourcesAsync(doc.Id, req.RequestIds, ct);

            // Fulfillment takibi: FulfilledByPurchase artır
            var linesWithTracking = validLines
                .Where(l => l.RequestLineId.HasValue && l.RequestLineId.Value > 0)
                .GroupBy(l => l.RequestLineId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Qty));

            if (linesWithTracking.Count > 0 && req.RequestIds?.Count > 0)
            {
                var allLines = new Dictionary<int, CalibraHub.Application.Contracts.DocumentLineDto>();
                foreach (var rid in req.RequestIds)
                {
                    var lines2 = await _documentService.GetQuoteLinesAsync(rid, ct);
                    foreach (var l in lines2) allLines[l.Id] = l;
                }
                foreach (var (reqLineId, purQty) in linesWithTracking)
                {
                    if (!allLines.TryGetValue(reqLineId, out var ln)) continue;
                    var newByPurchase = ln.FulfilledByPurchase + purQty;
                    await _documentRepo.UpdateLineFulfillmentAsync(reqLineId, ln.FulfilledFromStock, newByPurchase, ct);
                }
            }

            return Json(new { ok = true, docNo = doc.DocumentNumber, docId = doc.Id });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>
    /// İhtiyaç kayıtlarını "Converted" statüsüne alır (ihtiyaç kapatma).
    /// POST /Purchase/CloseRequests
    /// </summary>
    [HttpPost("/Purchase/CloseRequests")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseRequests([FromBody] CloseRequestsModel req, CancellationToken ct)
    {
        if (req?.RequestIds == null || req.RequestIds.Count == 0)
            return Json(new { ok = false, error = "Kapatılacak kayıt belirtilmedi." });

        var closed = new List<int>();
        var errors = new List<string>();
        foreach (var id in req.RequestIds)
        {
            var (ok, err) = await _documentService.ChangeStatusAsync(id, "Cancelled", ct);
            if (ok) closed.Add(id);
            else    errors.Add($"#{id}: {err}");
        }

        return Json(new { ok = errors.Count == 0, closed, errors });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Satın Alma Talebi — Wizard
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Satın Alma Talebi oluşturma sihirbazı.
    /// GET /Purchase/PurchaseRequestWizard
    /// </summary>
    [HttpGet("/Purchase/PurchaseRequestWizard")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseDemand)]
    public IActionResult PurchaseRequestWizard()
    {
        ViewData["Title"] = "Satın Alma Talebi Oluştur";
        return View("~/Views/Purchase/PurchaseRequestWizard.cshtml");
    }

    /// <summary>
    /// Tüm açık İhtiyaç Kaydı kalemlerini düz liste olarak döner.
    /// GET /Purchase/AllOpenRequestLines?materialSearch=&amp;requestNumber=&amp;hasStock=
    /// </summary>
    [HttpGet("/Purchase/AllOpenRequestLines")]
    public async Task<IActionResult> AllOpenRequestLines(
        string? materialSearch, string? requestNumber, bool? hasStock, CancellationToken ct)
    {
        var s = _schema.Replace("]", "]]");
        var (seFilter, seParams) = await BuildStockEffectFilterAsync("smd", ct);

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();

        cmd.CommandText = $"""
            SELECT
                dl.[Id]               AS lineId,
                d.[Id]                AS documentId,
                d.[DocumentNumber]    AS docNumber,
                d.[DocumentDate]      AS docDate,
                i.[Id]                AS itemId,
                i.[MaterialCode]      AS materialCode,
                i.[MaterialName]      AS materialName,
                mu.[Name]             AS unitCode,
                dl.[Quantity]         AS quantity,
                dl.[FulfilledByPurchase] AS fulfilledByPurchase,
                dl.[FulfilledFromStock]  AS fulfilledFromStock,
                dl.[Notes]            AS lineNotes,
                ISNULL(stk.Balance, 0) AS stockBalance
            FROM [{s}].[DocumentLine]   dl
            INNER JOIN [{s}].[Document]       d   ON d.[Id] = dl.[DocumentId]
            INNER JOIN [{s}].[DocumentType]   dt  ON dt.[Id] = d.[DocumentTypeId]
            INNER JOIN [{s}].[Items]          i   ON i.[Id] = dl.[ItemId]
            LEFT  JOIN [{s}].[MeasureUnits]   mu  ON mu.[Id] = dl.[UnitId]
            LEFT  JOIN (
                SELECT sm.[ItemId],
                       SUM(CASE
                           WHEN sm.[MovementType] IN (2,3) AND sm.[LocationId]     IS NOT NULL THEN  sm.[Quantity]
                           WHEN sm.[MovementType] IN (1,3) AND sm.[FromLocationId] IS NOT NULL THEN -sm.[Quantity]
                           WHEN sm.[MovementType] = 4 AND sm.[LocationId]     IS NOT NULL THEN  sm.[Quantity]
                           WHEN sm.[MovementType] = 4 AND sm.[FromLocationId] IS NOT NULL THEN -sm.[Quantity]
                           ELSE 0
                       END) AS Balance
                FROM [{s}].[DocumentLine] sm
                INNER JOIN [{s}].[Document] smd ON smd.[id] = sm.[DocumentId]
                WHERE sm.[MovementType] IS NOT NULL AND smd.[IsActive] = 1{seFilter}
                GROUP BY sm.[ItemId]
            ) stk ON stk.[ItemId] = i.[Id]
            WHERE dt.[code] = N'alis_talebi'
              AND d.[Status] NOT IN (N'Cancelled', N'Closed', N'Converted')
              AND dl.[Quantity] > ISNULL(dl.[FulfilledByPurchase], 0)
              AND (@MatSearch IS NULL OR i.[MaterialCode] LIKE @MatSearch OR i.[MaterialName] LIKE @MatSearch)
              AND (@DocNo IS NULL OR d.[DocumentNumber] LIKE @DocNo)
            ORDER BY d.[DocumentDate] DESC, d.[DocumentNumber], dl.[Id];
            """;

        var matParam = string.IsNullOrWhiteSpace(materialSearch)
            ? (object)System.DBNull.Value
            : $"%{materialSearch.Trim()}%";
        var docNoParam = string.IsNullOrWhiteSpace(requestNumber)
            ? (object)System.DBNull.Value
            : $"%{requestNumber.Trim()}%";

        cmd.Parameters.Add(new SqlParameter("@MatSearch", matParam));
        cmd.Parameters.Add(new SqlParameter("@DocNo",     docNoParam));
        foreach (var p in seParams) cmd.Parameters.Add(p);

        var result = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var qty        = r.GetDecimal(r.GetOrdinal("quantity"));
            var fulfilled  = r.IsDBNull(r.GetOrdinal("fulfilledByPurchase")) ? 0m : r.GetDecimal(r.GetOrdinal("fulfilledByPurchase"));
            var fromStock  = r.IsDBNull(r.GetOrdinal("fulfilledFromStock"))  ? 0m : r.GetDecimal(r.GetOrdinal("fulfilledFromStock"));
            var stockBal   = r.GetDecimal(r.GetOrdinal("stockBalance"));
            var remaining  = qty - fulfilled;

            if (hasStock == true  && stockBal <= 0) continue;
            if (hasStock == false && stockBal > 0)  continue;

            result.Add(new
            {
                lineId              = r.GetInt32(r.GetOrdinal("lineId")),
                documentId          = r.GetInt32(r.GetOrdinal("documentId")),
                docNumber           = r.GetString(r.GetOrdinal("docNumber")),
                docDate             = r.GetDateTime(r.GetOrdinal("docDate")).ToString("dd.MM.yyyy"),
                itemId              = r.GetInt32(r.GetOrdinal("itemId")),
                materialCode        = r.IsDBNull(r.GetOrdinal("materialCode")) ? null : r.GetString(r.GetOrdinal("materialCode")),
                materialName        = r.IsDBNull(r.GetOrdinal("materialName")) ? null : r.GetString(r.GetOrdinal("materialName")),
                unitCode            = r.IsDBNull(r.GetOrdinal("unitCode"))     ? null : r.GetString(r.GetOrdinal("unitCode")),
                quantity            = qty,
                fulfilledByPurchase = fulfilled,
                fulfilledFromStock  = fromStock,
                remaining,
                stockBalance        = stockBal,
                lineNotes           = r.IsDBNull(r.GetOrdinal("lineNotes")) ? null : r.GetString(r.GetOrdinal("lineNotes")),
            });
        }

        return Json(result);
    }

    /// <summary>
    /// Seçilen İhtiyaç Kaydı kalemlerinden Satın Alma Talebi belgesi oluşturur.
    /// POST /Purchase/CreatePurchaseDemand
    /// Body: { lineIds: [int], notes: string? }
    /// </summary>
    [HttpPost("/Purchase/CreatePurchaseDemand")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePurchaseDemand(
        [FromBody] CreatePurchaseDemandRequest req, CancellationToken ct)
    {
        try
        {
            // İki giriş şekli: Lines (kalem başına miktar) veya LineIds (kalan miktar kadar)
            var inputByLineId = new Dictionary<int, PurchaseDemandLineInput>();
            if (req?.Lines?.Count > 0)
                foreach (var l in req.Lines.Where(l => l.Qty > 0))
                    inputByLineId[l.LineId] = l;
            else if (req?.LineIds?.Count > 0)
                foreach (var id in req.LineIds)
                    inputByLineId[id] = new PurchaseDemandLineInput(id, 0m); // 0 = kalan miktar kullan

            if (inputByLineId.Count == 0)
                return Json(new { ok = false, error = "Kalem seçilmedi." });

            var docType = await _documentTypeRepo.GetByCodeAsync("satin_alma_talebi", ct);
            if (docType == null)
                return Json(new { ok = false, error = "satin_alma_talebi belge tipi tanımlı değil." });

            // Seçilen kalemleri belgelerine göre grupla — DocumentSource için
            var s         = _schema.Replace("]", "]]");
            var lineIds   = inputByLineId.Keys.ToList();
            var paramList = string.Join(",", lineIds.Select((_, i) => $"@lid{i}"));

            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            await using var cmdFetch = conn.CreateCommand();
            cmdFetch.CommandText = $"""
                SELECT dl.[Id], dl.[DocumentId], dl.[ItemId], dl.[UnitId], dl.[Quantity],
                       ISNULL(dl.[FulfilledByPurchase],0), ISNULL(dl.[FulfilledFromStock],0),
                       dl.[CombinationId], dl.[Notes]
                FROM [{s}].[DocumentLine] dl
                WHERE dl.[Id] IN ({paramList});
                """;
            for (var i = 0; i < lineIds.Count; i++)
                cmdFetch.Parameters.Add(new SqlParameter($"@lid{i}", lineIds[i]));

            var lineRows = new List<(int Id, int DocId, int ItemId, int? UnitId,
                decimal Qty, decimal FulfilledByPurchase, decimal FulfilledFromStock,
                int? CombinationId, string? Notes)>();

            await using var rdr = await cmdFetch.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
            {
                lineRows.Add((
                    rdr.GetInt32(0),
                    rdr.GetInt32(1),
                    rdr.GetInt32(2),
                    rdr.IsDBNull(3) ? (int?)null : rdr.GetInt32(3),
                    rdr.GetDecimal(4),
                    rdr.GetDecimal(5),
                    rdr.GetDecimal(6),
                    rdr.IsDBNull(7) ? (int?)null : rdr.GetInt32(7),
                    rdr.IsDBNull(8) ? null : rdr.GetString(8)
                ));
            }
            await rdr.DisposeAsync();

            if (lineRows.Count == 0)
                return Json(new { ok = false, error = "Seçilen kalemler bulunamadı." });

            var sourceDocIds = lineRows.Select(l => l.DocId).Distinct().ToList();

            var guardError = await CheckFulfillmentApprovalGuardAsync(sourceDocIds, ct);
            if (guardError != null)
                return Json(new { ok = false, error = guardError });

            // Talep miktarı: kullanıcı miktarı verdiyse o, vermediyse kalan
            // (Quantity − FulfilledFromStock − FulfilledByPurchase).
            var demandLines = new List<(int LineId, int ItemId, int? UnitId, decimal Qty, int? CombinationId, string? Notes,
                decimal FulfilledFromStock, decimal FulfilledByPurchase)>();
            foreach (var lr in lineRows)
            {
                var input     = inputByLineId[lr.Id];
                var remaining = Math.Max(0m, lr.Qty - lr.FulfilledFromStock - lr.FulfilledByPurchase);
                var qty       = input.Qty > 0 ? input.Qty : remaining;
                if (qty <= 0) continue;
                demandLines.Add((lr.Id, lr.ItemId, lr.UnitId, qty, lr.CombinationId,
                    string.IsNullOrWhiteSpace(input.Notes) ? lr.Notes : input.Notes,
                    lr.FulfilledFromStock, lr.FulfilledByPurchase));
            }

            if (demandLines.Count == 0)
                return Json(new { ok = false, error = "Seçilen kalemlerde talep edilecek kalan miktar yok — tümü zaten karşılanmış." });

            // Notes'a kaynak belge no'larını ekle
            string? notes = req!.Notes;
            if (sourceDocIds.Count > 0)
            {
                var nums = new List<string>();
                foreach (var rid in sourceDocIds)
                {
                    var srcDoc = await _documentService.GetQuoteByIdAsync(rid, ct);
                    if (srcDoc != null) nums.Add(srcDoc.DocumentNumber);
                }
                if (nums.Count > 0)
                {
                    var refLine = "Kaynak: " + string.Join(", ", nums);
                    notes = string.IsNullOrWhiteSpace(notes) ? refLine : refLine + "\n" + notes;
                }
            }

            var saveReq = new CalibraHub.Application.Contracts.SaveDocumentRequest(
                Id:              null,
                DocumentDate:    DateTime.Today,
                ValidUntil:      null,
                ContactId:       null,
                ContactName:     null,
                ContactAddress:  null,
                SalesRepId:      null,
                CurrencyId:      1,
                DiscountRate:    0m,
                TaxRate:         0m,
                PaymentTerms:    null,
                DeliveryTerms:   null,
                DeliveryAddress: null,
                Notes:           notes,
                Lines:           demandLines.Select(l => new CalibraHub.Application.Contracts.SaveDocumentLineRequest(
                    Id:            null,
                    ItemId:        l.ItemId,
                    UnitId:        l.UnitId,
                    Quantity:      l.Qty,
                    UnitPrice:     0m,
                    DiscountRate:  0m,
                    CombinationId: l.CombinationId,
                    LocationId:    null,
                    Notes:         l.Notes
                )).ToList(),
                DocumentTypeId:  docType.Id,
                FromRequestId:   sourceDocIds.FirstOrDefault()
            );

            var (success, error, doc, _) = await _documentService.SaveQuoteAsync(saveReq, CurrentUserId(), User?.Identity?.Name, ct);
            if (!success || doc == null)
                return Json(new { ok = false, error = error ?? "Belge oluşturulamadı." });

            // DocumentSource bağlantıları — her kaynak belge için.
            // Yön: AddAsync(türetilen=talep doc.Id, kaynak=İhtiyaç srcId). (2026-07-08 yön düzeltmesi)
            foreach (var srcId in sourceDocIds)
                await _docSourceRepo.AddAsync(doc.Id, srcId, ct);

            // FulfilledByPurchase artır — FulfilledFromStock korunur (0'a EZME).
            foreach (var dl in demandLines)
            {
                await _documentRepo.UpdateLineFulfillmentAsync(
                    dl.LineId, dl.FulfilledFromStock, dl.FulfilledByPurchase + dl.Qty, ct);
            }

            return Json(new { ok = true, docNo = doc.DocumentNumber, docId = doc.Id });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    private static string TranslateStatus(string status) => status switch
    {
        "Draft"     => "Taslak",
        "Sent"      => "Gonderildi",
        "Approved"  => "Onaylandi",
        "Rejected"  => "Reddedildi",
        "Cancelled" => "Iptal",
        "Closed"    => "Kapali",
        _           => status,
    };

    private static string StatusColor(string status) => status switch
    {
        "Draft"     => "slate",
        "Sent"      => "blue",
        "Approved"  => "emerald",
        "Rejected"  => "rose",
        "Cancelled" => "slate",
        "Closed"    => "indigo",
        _           => "slate",
    };

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    /// <summary>
    /// cbv_FulfillmentLineExtras view'ındaki ek kolon isimlerini keşfeder (DocumentId ve LineId hariç).
    /// View yoksa veya kolon yoksa boş liste döner.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetFulfillmentExtraColumnsAsync(CancellationToken ct)
    {
        var sl = _schema.Replace("'", "''");
        try
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            await using var cmd  = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = '{sl}' AND TABLE_NAME = 'cbv_FulfillmentLineExtras'
                  AND COLUMN_NAME NOT IN ('DocumentId', 'LineId')
                ORDER BY ORDINAL_POSITION;
                """;
            var cols = new List<string>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) cols.Add(r.GetString(0));
            return cols;
        }
        catch { return []; }
    }

    /// <summary>
    /// Onay akışında (Pending) olan İhtiyaç Kaydı belge ID'lerini döner.
    /// ApprovalInstance.DocumentId (INT FK) üzerinden okunur.
    /// </summary>
    private async Task<IReadOnlyList<int>> GetPendingApprovalDocIdsAsync(CancellationToken ct)
    {
        var sl = _schema.Replace("'", "''");
        try
        {
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            await using var cmd  = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT [DocumentId] FROM [{sl}].[ApprovalInstance]
                WHERE [Status] = N'Pending' AND [IsActive] = 1
                  AND [DocumentId] IS NOT NULL
                """;
            var result = new List<int>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                if (!r.IsDBNull(0)) result.Add(r.GetInt32(0));
            }
            return result;
        }
        catch { return []; }
    }

    // ── Sütun ayarları (Karşılama Merkezi — flat view) ──────────────────────
    [HttpGet]
    public async Task<IActionResult> GetFlatColConfig(CancellationToken ct)
    {
        var uid = CurrentUserId();
        if (!uid.HasValue) return Json(new { config = (string?)null });
        var json = await _userSettingRepo.GetAsync(uid.Value, FlatColCfgKey, ct);
        return Json(new { config = json });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveFlatColConfig([FromBody] Fc3SaveColConfigRequest request, CancellationToken ct)
    {
        var uid = CurrentUserId();
        if (!uid.HasValue) return Json(new { ok = false });
        await _userSettingRepo.SetAsync(uid.Value, FlatColCfgKey, request.Config, ct);
        return Json(new { ok = true });
    }
}

public sealed record Fc3SaveColConfigRequest(string? Config);
