using CalibraHub.Application.Constants;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Approval.EntityTypes;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Web.Models;
using CalibraHub.Web.Models.Purchase;
using CalibraHub.Web.Models.Sales;
using static CalibraHub.Web.Helpers.AuditLogActionHelper;
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
    private readonly IApprovalFlowService _approvalFlowService;
    private readonly ILogger<PurchaseController> _logger;
    private readonly string _schema;
    private const string FlatColCfgKey = "ui.fc3.col-cfg-flat";

    private readonly IPurchaseInvoiceRepository _purchaseInvoices;

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
        IApprovalFlowService approvalFlowService,
        CalibraDatabaseOptions dbOptions,
        IPurchaseInvoiceRepository purchaseInvoices,
        ILogger<PurchaseController> logger)
    {
        _logger = logger;
        _purchaseInvoices  = purchaseInvoices;
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
        _approvalFlowService = approvalFlowService;
        _schema = string.IsNullOrWhiteSpace(dbOptions.Schema) ? "dbo" : dbOptions.Schema.Trim();
    }

    private int? CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>
    /// SmartBoard extraActions "Kopyala" ögesi (PageComment Seq 19, 2026-07-21) — SalesController
    /// tarafındaki TEK paylaşılan CopyDocumentJson endpoint'ine gider (Delete'in DeleteDocumentJson
    /// için zaten kurduğu "tek merkez, iki controller paylaşır" deseniyle aynı — tip SUNUCUDA
    /// kaynak belgeden çözülür). SalesController.BuildCopyAction ile birebir aynı gövde (private
    /// static, controller'lar arası paylaşılamadığı için — mevcut BuildAuditLogAction öncesi
    /// duplikasyon deseniyle tutarlı).
    /// </summary>
    private static object BuildCopyAction(int documentId) => new
    {
        id = "copy",
        label = "Kopyala",
        icon = "Copy",
        color = "indigo",
        type = "api-post",
        url = $"/Sales/CopyDocumentJson?id={documentId}",
        apiUrl = $"/Sales/CopyDocumentJson?id={documentId}",
        apiMethod = "POST",
    };

    /// <summary>
    /// Belge LİSTE satır menüsüne "kaydın edit ekranını autoAction ile aç" 3 işlemi
    /// (Durum Değiştir / Tüm Ürünlerin Maliyeti / Onay Süreci) — 2026-08-02, kullanıcı
    /// isteği: edit ekranı İşlemler menüsündeki işlemler liste satır menüsünde de olsun.
    /// Tümü /Sales/DocumentEdit'i (5 belge tipi paylaşır) autoAction query'siyle açar;
    /// edit ekranı yükleme sonrası ilgili işlemi otomatik tetikler.
    /// </summary>
    // "Tüm Ürünlerin Maliyeti" BURADA YOK: kullanıcı kararı gereği maliyet yalnız Satış
    // Teklifi adımında görünür (PageComment Seq 1112). Alış belgelerinde bu aksiyonun
    // bırakılması, edit ekranındaki kısıtı autoAction=costs deep-link'iyle delerdi.
    private static object[] BuildRecordOperationActions(int id) => new object[]
    {
        new { label = "Durum Değiştir",         icon = "Clock",     color = "violet", url = $"/Sales/DocumentEdit?id={id}&autoAction=status" },
        new { label = "Onay Süreci",            icon = "GitBranch", color = "sky",    url = $"/Sales/DocumentEdit?id={id}&autoAction=approval" },
    };

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
                              ApprovalParameters.FormCode, ApprovalParameters.UseKey("PurchaseRequest"), ct) != "false"
                          && await _companyParams.GetStringAsync(
                              ApprovalParameters.FormCode, ApprovalParameters.EnabledKey("PurchaseRequest"), ct) != "false";

        // Açık (Pending) onay süreci olan belgeler — parametre sonradan kapatılsa bile önce
        // onay akışını tamamlamalı. Bu kontrol her durumda uygulanır.
        // FAIL-CLOSED: pending listesi DB hatasıyla okunamazsa karşılamayı ENGELLE (boş sanıp
        // onay kapısını atlama — GetPendingApprovalDocIdsAsync artık throw ediyor).
        HashSet<int> pendingIds;
        try { pendingIds = (await GetPendingApprovalDocIdsAsync(ct)).ToHashSet(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Purchase.CheckFulfillmentApprovalGuard] Bekleyen onay durumu okunamadı — fail-closed (karşılama engellendi).");
            return "Onay durumu doğrulanamadı — lütfen tekrar deneyin.";
        }

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

    /// <summary>
    /// Yeni oluşturulan bir belge için aktif bir onay akışı varsa otomatik başlatır —
    /// <see cref="CalibraHub.Application.Services.DocumentService.SaveQuoteAsync"/> içindeki
    /// auto-start bloğuyla AYNI mantık (kind çözümleme + APPROVAL_ENABLED_{kind} parametre
    /// kontrolü + MatchFlowAsync + StartAsync). Stok belgeleri (STOCK_IN/STOCK_OUT/TRANSFER)
    /// <see cref="IStockDocRepository"/> üzerinden raw SQL ile yazılır — DocumentService'ten
    /// HİÇ geçmez, dolayısıyla bu belgeler için otomatik onay tetikleme daha önce hiç yoktu
    /// (2026-07-21 keşfi). Bu helper yalnızca Depodan Karşıla akışında oluşan Ambar Çıkış
    /// Fişi (depo_cikis) için o boşluğu kapatır. "depo_cikis" DocumentEntityTypes.Definitions
    /// listesinde yok → her zaman wildcard "Document" (Tüm Belgeler) kind'ına çözülür; bu,
    /// diğer eşlenmemiş belge türleri için de geçerli olan mevcut davranıştır (yeni bir
    /// özel durum icat edilmedi). Hata belge kaydını asla bozmaz (DocumentService ile aynı
    /// "sessizce geç" kuralı).
    /// </summary>
    private async Task<bool> TryAutoStartApprovalAsync(int documentId, string documentTypeCode, CancellationToken ct)
    {
        try
        {
            var kind = DocumentEntityTypes.ResolveKind(documentTypeCode);

            var approvalEnabled = true;
            if (kind != DocumentEntityTypes.WildcardKind)
            {
                // Ana anahtar: onay sistemi bu belge türünde kullanılmıyorsa hiçbir onay davranışı devreye girmez.
                var useApproval = await _companyParams.GetBoolAsync(
                    ApprovalParameters.FormCode, ApprovalParameters.UseKey(kind), ct) ?? true;
                if (!useApproval) return false;

                approvalEnabled = await _companyParams.GetBoolAsync(
                    ApprovalParameters.FormCode, ApprovalParameters.EnabledKey(kind), ct) ?? true;
            }
            if (!approvalEnabled) return false;

            var flow = await _approvalFlowService.MatchFlowAsync(kind, null, null, null, ct);
            if (flow is null) return false;

            var userName = User.FindFirstValue(ClaimTypes.Name) ?? "system";
            await _approvalFlowService.StartAsync(
                new StartApprovalRequest(
                    DocumentId:      documentId,
                    FlowId:          flow.Id,
                    StartedBy:       userName,
                    StartedByUserId: CurrentUserId()),
                ct);
            return true;
        }
        catch (Exception ex)
        {
            // Akış başlatma hatası belge kaydını iptal etmez — belge zaten kaydedildi.
            // Ama sessizce yutma (CLAUDE.md kural #2): yanlış yapılandırılmış akış / DB hatası
            // teşhis edilebilsin diye logla, istemciye jenerik davran.
            _logger.LogWarning(ex, "[Purchase] Depodan Karsila auto-start onay baslatilamadi: Document {DocumentId}, tip {TypeCode}", documentId, documentTypeCode);
            return false;
        }
    }

    // ── STOCK_DOC_CONSOLIDATE (2026-07-24) — karşı fiş satır kümülesi ──────────────────────
    // FulfillFromStock / CreateStockIssue / CreateTransfer üçü de aynı deseni kullanır: parametre
    // açıkken aynı (ItemId, FromLocationId, ToLocationId, CombinationId, UnitId) satırları tek
    // fiş satırında toplar. Kapalıyken (varsayılan) davranış birebir korunur.

    /// <summary>
    /// Kümüleme girdisi — Transfer/StockIssue/FulfillFromStock kendi DTO'sundan buna map eder.
    /// <see cref="ToLocationId"/> yalnız Transfer'de anlamlıdır (StockIssue/FulfillFromStock her
    /// zaman null geçer — kümüleme anahtarına katılımı bu yüzden no-op'tur).
    /// </summary>
    private sealed record ConsolidationInputLine(
        int ItemId, int? UnitId, decimal Qty, int? FromLocationId, int? ToLocationId,
        int? CombinationId, string? Notes, int? RequestLineId);

    /// <summary>
    /// Kümüleme sonucu — <see cref="RequestLineIds"/> bu fiziksel satırı besleyen tüm İhtiyaç
    /// Kaydı satırlarını (distinct) taşır; SqlStockDocRepository.SaveDirectDocAsync bunu kullanarak
    /// karşılama defteri kayıtlarının RefDocLineId'sini bu satıra bağlar.
    /// </summary>
    private sealed record ConsolidatedStockLine(
        int ItemId, int? UnitId, decimal Qty, int? FromLocationId, int? ToLocationId,
        int? CombinationId, string? Notes, IReadOnlyList<int> RequestLineIds);

    /// <summary>
    /// Aynı (ItemId, FromLocationId, ToLocationId, CombinationId, UnitId) kombinasyonuna sahip
    /// satırları TEK satırda toplar (Qty SUM). Farklı depo/hedef depo/kombinasyon/birim ayrı
    /// fiziksel stok anlamına geldiği için her zaman ayrı satır kalır (kullanıcı kararı — Transfer'de
    /// ToLocationId de anahtara dahil: farklı hedef depo ayrı satır ZORUNLU). Notes: kümülelenen
    /// satırların benzersiz/boş-olmayan notları "; " ile birleştirilir (küçük karar, bkz. rapor).
    /// </summary>
    private static List<ConsolidatedStockLine> ConsolidateStockLines(IEnumerable<ConsolidationInputLine> lines) =>
        lines
            .GroupBy(l => (l.ItemId, l.FromLocationId, l.ToLocationId, l.CombinationId, l.UnitId))
            .Select(g => new ConsolidatedStockLine(
                ItemId:         g.Key.ItemId,
                UnitId:         g.Key.UnitId,
                Qty:            g.Sum(x => x.Qty),
                FromLocationId: g.Key.FromLocationId,
                ToLocationId:   g.Key.ToLocationId,
                CombinationId:  g.Key.CombinationId,
                Notes:          CombineConsolidatedNotes(g),
                RequestLineIds: g.Where(x => x.RequestLineId is > 0)
                                 .Select(x => x.RequestLineId!.Value)
                                 .Distinct()
                                 .ToList()))
            .ToList();

    private static string? CombineConsolidatedNotes(IEnumerable<ConsolidationInputLine> group)
    {
        var distinct = group.Select(x => x.Notes)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count == 0) return null;
        var combined = string.Join("; ", distinct);
        // DocumentLine.Notes NVARCHAR(500) — kumule birlestirmesi taban kolonu ASMAMALI, yoksa
        // SQL truncation -> tum transaction rollback -> fis olusmaz (review Bulgu 1). 500'e kirp.
        return combined.Length <= 500 ? combined : combined.Substring(0, 499) + "…";
    }

    /// <summary>
    /// STOCK_DOC_CONSOLIDATE parametresine göre karşı fiş satırlarını üretir. Kapalıyken
    /// (varsayılan) her girdi satırı kendi fiş satırını korur — mevcut davranış birebir,
    /// <c>RequestLineIds</c> hiç set edilmez (SaveDirectDocAsync'teki RefDocLineId zenginleştirmesi
    /// devre dışı kalır, bugünkü null davranışı sürer). Açıkken ConsolidateStockLines ile kümülenir;
    /// her kümüle satır kendi RequestLineIds listesini taşır.
    /// </summary>
    private static List<SaveStockDocLineRequest> BuildStockDocLines(
        IEnumerable<ConsolidationInputLine> inputLines, bool consolidate)
    {
        if (!consolidate)
        {
            return inputLines.Select(l => new SaveStockDocLineRequest(
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
                UnitCost:       null)).ToList();
        }

        return ConsolidateStockLines(inputLines).Select(c => new SaveStockDocLineRequest(
            Id:             null,
            ItemId:         c.ItemId,
            MaterialCode:   null,
            MaterialName:   null,
            UnitId:         c.UnitId,
            Qty:            c.Qty,
            CombinationId:  c.CombinationId,
            Notes:          c.Notes,
            FromLocationId: c.FromLocationId,
            ToLocationId:   c.ToLocationId,
            UnitCost:       null,
            RequestLineIds: c.RequestLineIds)).ToList();
    }

    [HttpGet("/Purchase/Requests")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseRequest)]
    public Task<IActionResult> Requests(CancellationToken ct) =>
        RenderListAsync("alis_talebi",   "PURCHASE_REQUEST_EDIT", "İhtiyaç Kayıtları",
                        "ihtiyaç",       "/Purchase/Edit?type=purchase_request", "amber", ct,
                        helpKey: "purchase-requests");

    [HttpGet("/Purchase/Quotes")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseQuote)]
    public Task<IActionResult> Quotes(CancellationToken ct) =>
        RenderListAsync("alis_teklifi",  "PURCHASE_QUOTE_EDIT",   "Satin Alma Teklifleri",
                        "teklif",        "/Purchase/Edit?type=purchase_quote",   "blue",  ct,
                        helpKey: "purchase-quotes");

    [HttpGet("/Purchase/Orders")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseOrder)]
    public Task<IActionResult> Orders(CancellationToken ct) =>
        RenderListAsync("alis_siparisi", "PURCHASE_ORDER_EDIT",   "Satin Alma Siparisleri",
                        "siparis",       "/Purchase/Edit?type=purchase_order",   "emerald", ct,
                        helpKey: "purchase-orders");

    [HttpGet("/Purchase/PurchaseDemands")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseDemand)]
    public Task<IActionResult> PurchaseDemands(CancellationToken ct) =>
        RenderListAsync("satin_alma_talebi", "PURCHASE_DEMAND_EDIT", "Satın Alma Talepleri",
                        "talep", "/Purchase/Edit?type=purchase_demand", "violet", ct,
                        newUrl: "/Purchase/PurchaseRequestWizard", helpKey: "purchase-demands");

    [HttpGet("/Purchase/Deliveries")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseDelivery)]
    public Task<IActionResult> Deliveries(CancellationToken ct) =>
        RenderListAsync("alis_irsaliyesi", "PURCHASE_DELIVERY_EDIT", "Alış İrsaliyeleri",
                        "irsaliye", "/Purchase/Edit?type=purchase_delivery", "rose", ct,
                        helpKey: "purchase-deliveries");

    // ── Gelen e-belgeden alış faturası (3 yol) ───────────────────────────────

    /// <summary>
    /// Eşleştirme ekranındaki stok seçici için hafif arama ucu — ItemDocumentLockSearchItems
    /// ile AYNI servis metodunu kullanır (yeni bir arama mantığı türetilmez).
    /// </summary>
    [HttpGet("/Purchase/InvoiceItemSearch")]
    public async Task<IActionResult> InvoiceItemSearch(string? term, CancellationToken ct)
    {
        try
        {
            var (items, _) = await _logisticsService.GetItemsPagedAsync(term, 0, 20, ct);
            return Json(new { items = items.Select(x => new { id = x.Id, code = x.Code, name = x.Name }) });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AlışFatura] Stok arama başarısız (term={Term}).", term);
            return Json(new { items = Array.Empty<object>() });
        }
    }


    /// <summary>Eşleştirme ekranı. incomingId = gelen e-belge kaydı.</summary>
    [HttpGet("/Purchase/InvoiceFromEDocument")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseInvoice)]
    public async Task<IActionResult> InvoiceFromEDocument(int incomingId, string? mode, CancellationToken ct)
    {
        var data = await _purchaseInvoices.GetCandidatesAsync(incomingId, mode ?? "direct", ct);
        if (data is null) return NotFound();

        ViewData["Title"] = $"Faturaya Aktar — {data.DocumentNumber}";
        ViewData["HelpKey"] = "purchase-invoices";
        return View("~/Views/Purchase/InvoiceFromEDocument.cshtml", data);
    }

    /// <summary>Mod değişince ekran bu uçtan tazelenir (sayfa yeniden yüklenmez).</summary>
    [HttpGet("/Purchase/EDocumentInvoiceCandidates")]
    public async Task<IActionResult> EDocumentInvoiceCandidates(int incomingId, string? mode, CancellationToken ct)
    {
        var data = await _purchaseInvoices.GetCandidatesAsync(incomingId, mode ?? "direct", ct);
        return data is null
            ? Json(new { success = false, message = "e-Belge bulunamadı." })
            : Json(new { success = true, data });
    }

    [HttpPost("/Purchase/CreateInvoiceFromEDocument")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseInvoice)]
    public async Task<IActionResult> CreateInvoiceFromEDocument(
        [FromBody] CalibraHub.Application.Contracts.CreatePurchaseInvoiceRequest request, CancellationToken ct)
    {
        // Gövde bağlanamadıysa jenerik "bir hata oluştu" DEMEZ: sebep açıkça söylenir,
        // aksi halde istemci hatası sunucu hatası gibi görünür (teşhis kaybı).
        if (request is null)
            return Json(new { success = false, message = "İstek gövdesi okunamadı (geçersiz JSON veya eksik alan)." });

        try
        {
            var userId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid)
                ? uid : (int?)null;
            var result = await _purchaseInvoices.CreateAsync(request, userId, ct);
            return Json(new
            {
                success = true,
                documentId = result.DocumentId,
                documentNumber = result.DocumentNumber,
                lineCount = result.LineCount,
                stockAffected = result.StockAffected,
                editUrl = $"/Purchase/Edit?type=purchase_invoice&id={result.DocumentId}",
            });
        }
        catch (InvalidOperationException ex)
        {
            // İş kuralı ihlali — mesaj kullanıcıya AÇIKÇA gösterilir (sessiz jenerik hata yok).
            return Json(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AlışFatura] e-Belgeden fatura oluşturulamadı (IncomingId={Id}).",
                request?.IncomingDocumentId);
            return Json(new { success = false, message = "Fatura oluşturulurken bir hata oluştu." });
        }
    }

    [HttpGet("/Purchase/Invoices")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseInvoice)]
    public Task<IActionResult> Invoices(CancellationToken ct) =>
        RenderListAsync("alis_faturasi", "PURCHASE_INVOICE_EDIT", "Alış Faturaları",
                        "fatura", "/Purchase/Edit?type=purchase_invoice", "emerald", ct,
                        helpKey: "purchase-invoices");

    [HttpGet("/Purchase/InvoicesBoardConfig")]
    public async Task<IActionResult> InvoicesBoardConfig(CancellationToken ct)
    {
        var config = await BuildPurchaseBoardAsync(
            "alis_faturasi", "PURCHASE_INVOICE_EDIT", "Alış Faturaları",
            "fatura", "/Purchase/Edit?type=purchase_invoice", "emerald", ct);
        return Json(config);
    }

    [HttpGet("/Purchase/DeliveriesBoardConfig")]
    public async Task<IActionResult> DeliveriesBoardConfig(CancellationToken ct)
    {
        var config = await BuildPurchaseBoardAsync(
            "alis_irsaliyesi", "PURCHASE_DELIVERY_EDIT", "Alış İrsaliyeleri",
            "irsaliye", "/Purchase/Edit?type=purchase_delivery", "rose", ct);
        return Json(config);
    }

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
        string editUrl, string iconColor, CancellationToken ct, string? newUrl = null,
        string? helpKey = null)
    {
        var boardConfig = await BuildPurchaseBoardAsync(
            typeCode, formCode, title, entityWord, editUrl, iconColor, ct, newUrl);
        // Documents.cshtml ViewData["Title"]'i hardcode "Satis Teklifleri" yaziyor;
        // controller'dan override ile dogru baslik gosteriyoruz (tarayici tab + page header).
        ViewData["Title"]    = title;
        ViewData["FormCode"] = formCode;
        ViewData["DbInfo"]   = null;  // sales-spesifik HTML tablosunu gizle
        // F1 yardimi yalniz helpKey verilen listede aktif (ortak Documents.cshtml'e yayilmasin).
        if (!string.IsNullOrWhiteSpace(helpKey))
            ViewData["HelpKey"] = helpKey;
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
            "purchase_quote"    or "alis_teklifi"       => "purchase_quote",
            "purchase_order"    or "alis_siparisi"      => "purchase_order",
            "purchase_demand"   or "satin_alma_talebi"  => "purchase_demand",
            "purchase_delivery" or "alis_irsaliyesi"    => "purchase_delivery",
            "purchase_invoice"  or "alis_faturasi"      => "purchase_invoice",
            _                                            => "purchase_request",
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
            || (await _companyParams.GetStringAsync(
                    ApprovalParameters.FormCode, ApprovalParameters.UseKey("PurchaseRequest"), ct) != "false"
                && await _companyParams.GetStringAsync(
                    ApprovalParameters.FormCode, ApprovalParameters.EnabledKey("PurchaseRequest"), ct) != "false");
        var auditFormCode = DocumentTypeFormMap.Resolve(typeCode).Header;

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
            extraActionsList.Add(BuildCopyAction(doc.Id));
            if (string.Equals(typeCode, "alis_talebi", StringComparison.OrdinalIgnoreCase) && purchaseReqApprovalEnabled)
            {
                // "Onaya Gönder" — yalnız Draft belgelerde menüye eklenir; onay tetikleme
                // kapalıysa hiç gösterilmez (elle de onaya sokulamaz).
                // NOT (2026-07-21 fix): eski şekil `type:"api-post"` + `url:"...{id}"` yalnız
                // SmartCard sözleşmesiydi — bu board'lar tablo modunda render edildiğinden
                // SmartTableRow menüsü aksiyonu sessizce çalıştıramıyordu. Tablo menüsünün
                // anladığı sözleşme apiUrl (gerçek id) + apiMethod'dur; confirm/disabled
                // alanları menüde desteklenmediği için öğe Draft-dışında hiç gönderilmez,
                // tıklama doğrudan tetikler (Kopyala ve entegrasyon butonlarıyla tutarlı;
                // yanlış başlatma _WorkflowDrawer'daki CancelApproval ile geri alınabilir).
                var isDraft = string.Equals(doc.Status, "Draft", StringComparison.OrdinalIgnoreCase);
                if (isDraft)
                {
                    extraActionsList.Add(new
                    {
                        id        = "sendApproval",
                        label     = "Onaya Gönder",
                        icon      = "Send",
                        color     = "emerald",
                        apiUrl    = $"/ApprovalFlow/StartByDocument?documentId={doc.Id}",
                        apiMethod = "POST",
                    });
                }
            }
            extraActionsList.AddRange(BuildRecordOperationActions(doc.Id));
            extraActionsList.Add(BuildAuditLogAction(typeCode, doc.Id, auditFormCode));
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
                    label       = "Sil",
                    icon        = "Trash2",
                    apiUrl      = $"/Sales/DeleteDocumentJson?id={doc.Id}",
                    precheckUrl = $"/Sales/CanDeleteDocumentJson?id={doc.Id}",
                    confirm     = $"Bu {entityWord}i silmek istediginizden emin misiniz? ({doc.DocumentNumber})",
                },
                extraActions,
            });
        }

        var effectiveNewUrl = newUrl ?? editUrl;
        // alis_talebi listesinde "Karsilama Merkezi" header butonu eklenir.
        // openInTab + matchPath (2026-07-25, PageComment Seq 27): sol menüden "İhtiyaç Karşılama"ya
        // tıklanmışçasına davranması istendi — SmartBoard header action'ları önceden bunu
        // desteklemiyordu (yalnızca navigateInWorkspace ile aynı iframe içinde navigate ediyordu,
        // matchPath'i hiç okumuyordu). SmartBoard.jsx handleActionClick'e AuditLogActionHelper'daki
        // ile aynı openInTab desteği eklendi (bkz. o dosyanın XML doc'u) — matchPath = "/Purchase/
        // FulfillmentCenter" sayesinde ekran zaten açık bir sekmede ise yeni sekme yerine o sekmeye
        // odaklanılır, değilse yeni sekme açılır.
        var boardActions = string.Equals(typeCode, "alis_talebi", StringComparison.OrdinalIgnoreCase)
            ? new object[]
            {
                new {
                    id = "center", label = "Karşılama Merkezi", icon = "Layers", variant = "secondary", url = "/Purchase/FulfillmentCenter",
                    openInTab = new { title = "İhtiyaç Karşılama", matchPath = "/Purchase/FulfillmentCenter" },
                },
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
            "alis_irsaliyesi"   => "/Purchase/DeliveriesBoardConfig",
            "alis_faturasi"     => "/Purchase/InvoicesBoardConfig",
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

        // Salt-gösterim (board'da pending belgeleri işaretleme) — DB hatasında boş geç (fail-open
        // burada zararsız: gerçek karşılama guard'ı ayrıca fail-closed kontrol eder).
        IReadOnlyList<int> pendingApprovalDocIds;
        try { pendingApprovalDocIds = await GetPendingApprovalDocIdsAsync(ct); }
        catch { pendingApprovalDocIds = []; }
        ViewData["PendingApprovalDocIdsJson"] = JsonSerializer.Serialize(pendingApprovalDocIds, fcJsonOpts);

        // İhtiyaç Kaydı onay tetikleme açık mı → karşılama ekranında seçim kapısını belirler.
        // Açık: yalnızca Onaylı belgeler seçilebilir. Kapalı: tümü seçilebilir.
        ViewData["ApprovalRequired"] = await _companyParams.GetStringAsync(
                                           ApprovalParameters.FormCode, ApprovalParameters.UseKey("PurchaseRequest"), ct) != "false"
                                       && await _companyParams.GetStringAsync(
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
            // Sessizce yutma (CLAUDE.md kural #2) — ex daha once hic loglanmiyordu; server'a
            // logla, istemciye jenerik mesaj don.
            _logger.LogError(ex, "[Purchase.RequestLines] Kalem verisi cekilirken hata. RequestIds: {RequestIds}", string.Join(",", requestIds));
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

        // Asgari stok koruması: parametre açıkken depo bazında asgari (ItemLocation.MinStock)
        // görünen bakiyeden düşülür — FC "Stok" kolonu kullanılabilir miktarı gösterir.
        // (Genel asgari Items.MinStock, dağıtım tavanı olarak FulfillFromStock'ta uygulanır.)
        var respectMinStock = await _companyParams.GetBoolAsync(
            FulfillmentParameters.FormCode, FulfillmentParameters.RespectMinStockKey, ct) ?? false;
        var minJoin = respectMinStock
            ? $"LEFT JOIN [{s}].[ItemLocation] il ON il.[ItemId] = c.ItemId AND il.[LocationId] = c.LocationId"
            : "";
        var balExpr = respectMinStock ? "SUM(c.Bal) - ISNULL(MAX(il.[MinStock]), 0)" : "SUM(c.Bal)";

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
            SELECT c.ItemId, c.LocationId, loc.LocationName, {balExpr} AS Balance
            FROM Combined c
            LEFT JOIN [{s}].[Location] loc ON loc.Id = c.LocationId
            {minJoin}
            GROUP BY c.ItemId, c.LocationId, loc.LocationName
            HAVING {balExpr} > 0
            ORDER BY c.ItemId, {balExpr} DESC;
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
    /// PageComment Seq 18 (2026-07-21) — İhtiyaç Kaydı kalem "İşlemler" menüsündeki
    /// "Karşılama Detayı" için: bir İhtiyaç satırının (DocumentLine.Id = RequestLineId)
    /// karşılama defteri (DocumentLineFulfillment) kayıtlarını döner — hangi belge ne kadarını
    /// karşıladı, ne zaman, hâlâ aktif mi (ters çevrilmemiş mi). Salt-okunur; defteri
    /// DEĞİŞTİRMEZ. 2026-07-02 konsolidasyonu gereği RefDocId her zaman dbo.Document(Id) —
    /// transfer/ambar çıkışı da dahil tek JOIN yeterli (bkz. FulfillmentLedgerContracts.cs).
    /// GET /Purchase/GetLineFulfillmentEntriesJson?lineId=123
    /// </summary>
    [HttpGet("/Purchase/GetLineFulfillmentEntriesJson")]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseFulfillment)]
    public async Task<IActionResult> GetLineFulfillmentEntriesJson(int lineId, CancellationToken ct)
    {
        if (lineId <= 0) return Json(Array.Empty<object>());

        var s = _schema.Replace("]", "]]");
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT f.[Id], f.[FulfillmentType], f.[RefDocId], f.[Quantity], f.[Notes],
                   f.[IsActive], f.[Created],
                   d.[DocumentNumber], d.[DocumentDate], d.[Status] AS DocStatus,
                   ca.[AccountTitle] AS ContactName
              FROM [{s}].[DocumentLineFulfillment] f
              LEFT JOIN [{s}].[Document] d ON d.[Id] = f.[RefDocId]
              LEFT JOIN [{s}].[Contact]  ca ON ca.[Id] = d.[ContactId]
             WHERE f.[RequestLineId] = @LineId
             ORDER BY f.[IsActive] DESC, f.[Created] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@LineId", lineId));

        var result = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var kind = r.GetByte(r.GetOrdinal("FulfillmentType"));
            result.Add(new
            {
                id               = r.GetInt32(r.GetOrdinal("Id")),
                kind,
                kindLabel        = FulfillmentKindLabel((FulfillmentSourceKind)kind),
                refDocId         = r.IsDBNull(r.GetOrdinal("RefDocId")) ? (int?)null : r.GetInt32(r.GetOrdinal("RefDocId")),
                documentNumber   = r.IsDBNull(r.GetOrdinal("DocumentNumber")) ? null : r.GetString(r.GetOrdinal("DocumentNumber")),
                documentDate     = r.IsDBNull(r.GetOrdinal("DocumentDate")) ? (DateTime?)null : r.GetDateTime(r.GetOrdinal("DocumentDate")),
                documentStatus   = r.IsDBNull(r.GetOrdinal("DocStatus")) ? null : r.GetString(r.GetOrdinal("DocStatus")),
                contactName      = r.IsDBNull(r.GetOrdinal("ContactName")) ? null : r.GetString(r.GetOrdinal("ContactName")),
                quantity         = r.GetDecimal(r.GetOrdinal("Quantity")),
                notes            = r.IsDBNull(r.GetOrdinal("Notes")) ? null : r.GetString(r.GetOrdinal("Notes")),
                isActive         = r.GetBoolean(r.GetOrdinal("IsActive")),
                created          = r.GetDateTime(r.GetOrdinal("Created")),
            });
        }
        return Json(result);
    }

    /// <summary>Karşılama türü (FulfillmentSourceKind) → kullanıcıya gösterilecek Türkçe etiket.</summary>
    private static string FulfillmentKindLabel(FulfillmentSourceKind kind) => kind switch
    {
        FulfillmentSourceKind.Transfer       => "Depo Transferi",
        FulfillmentSourceKind.PurchaseQuote  => "Satın Alma Teklifi",
        FulfillmentSourceKind.PurchaseOrder  => "Satın Alma Siparişi",
        FulfillmentSourceKind.StockIssue     => "Ambar Çıkışı",
        FulfillmentSourceKind.PurchaseDemand => "Satın Alma Talebi",
        FulfillmentSourceKind.LegacyStock    => "Stok (Geçmiş Kayıt)",
        FulfillmentSourceKind.LegacyPurchase => "Satın Alma (Geçmiş Kayıt)",
        _                                     => "Bilinmeyen",
    };

    /// <summary>
    /// Depo transferi oluşturur ve kaynak İhtiyaç kaydına RefNo üzerinden bağlar.
    /// POST /Purchase/CreateTransfer
    /// Yanıt: { ok: true, docNo } veya { ok: false, error }
    /// </summary>
    [HttpPost("/Purchase/CreateTransfer")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.Transfer)]
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

            // Fulfillment takibi: RequestLineId gönderilmiş satırların FulfilledFromStock artır.
            // 2026-07-20 (Madde 2): entries belge kaydından ÖNCE hazırlanır (newDocId henüz yok —
            // PendingFulfillmentEntry RefDocId taşımaz) ve SaveAsync'e verilir; repo bunu belgenin
            // kendi transaction'ı İÇİNDE yazar (atomik — "belge var, defter yok" oluşamaz).
            var linesWithTracking = validLines
                .Where(l => l.RequestLineId.HasValue && l.RequestLineId.Value > 0)
                .GroupBy(l => l.RequestLineId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Qty));

            var allLines = new Dictionary<int, DocumentLineDto>();
            List<PendingFulfillmentEntry>? pendingEntries = null;
            if (linesWithTracking.Count > 0 && req.RequestIds?.Count > 0)
            {
                // Mevcut satır değerlerini request belgelerinden çek (N+1 ama küçük set)
                foreach (var rid in req.RequestIds)
                {
                    var lines2 = await _documentService.GetQuoteLinesAsync(rid, ct);
                    foreach (var l in lines2) allLines[l.Id] = l;
                }

                // allLines GEÇERLİLİK FİLTRESİ: yalnızca bildirilen İhtiyaç belgelerine ait
                // satırlar deftere girer (istemciden gelen rastgele LineId kabul edilmez).
                pendingEntries = linesWithTracking
                    .Where(kv => allLines.ContainsKey(kv.Key))
                    .Select(kv => new PendingFulfillmentEntry(kv.Key, FulfillmentSourceKind.Transfer, kv.Value))
                    .ToList();
            }

            // STOCK_DOC_CONSOLIDATE — aç/kapa parametre (varsayılan kapalı = mevcut davranış).
            var consolidate = await _companyParams.GetBoolAsync(
                FulfillmentParameters.FormCode, FulfillmentParameters.ConsolidateLinesKey, ct) ?? false;

            var saveReq = new SaveStockDocRequest(
                Id:             null,
                DocType:        "TRANSFER",
                DocNo:          null,
                DocDate:        DateTime.Today,
                FromLocationId: null,  // satır bazlı farklı lokasyonlar
                ToLocationId:   null,  // satır bazlı farklı lokasyonlar
                RefNo:          refNo,
                Notes:          req.Notes,
                Lines:          BuildStockDocLines(validLines.Select(l => new ConsolidationInputLine(
                    l.ItemId, l.UnitId, l.Qty, l.FromLocationId, l.ToLocationId, l.CombinationId, l.Notes, l.RequestLineId)),
                    consolidate),
                ArgeProjectId:  null
            );

            var (newDocId, docNo) = await _stockDocRepo.SaveAsync(saveReq, CurrentUserId(), ct, pendingEntries);

            // Belge soyağacı: transfer fişi ← kaynak İhtiyaç belge(ler)i (soyağacı akış görünümü)
            await LinkFulfillmentSourcesAsync(newDocId, req.RequestIds, ct);

            // İşlem logu (Madde 3, 2026-07-20): etkilenen İhtiyaç Kaydı satırlarının karşılama
            // durumu değişikliği. allLines = mutasyondan ÖNCEki snapshot; dokunulmayan satırlar
            // eski=yeni olduğu için otomatik atlanır (bkz. LogFulfillmentAuditAsync).
            if (allLines.Count > 0)
                await _documentService.LogFulfillmentAuditAsync(allLines, $"Depo transferi #{docNo}", ct);

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
            // Sessizce yutma (CLAUDE.md kural #2) — kumule SQL truncation dahil beklenmeyen hatalar
            // teshis edilebilsin diye logla, istemciye jenerik don.
            _logger.LogError(ex, "[Purchase] Karsilama aksiyonu sirasinda beklenmeyen hata.");
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    /// <summary>
    /// Ambar çıkış fişi oluşturur (STOCK_OUT) ve ihtiyaç satırlarının FulfilledFromStock günceller.
    /// POST /Purchase/CreateStockIssue
    /// 2026-07-25 UI'dan kaldırıldı (PageComment Seq 26 birleşimi) — FulfillmentCenter ekranındaki
    /// ayrı "Ambar Çıkış Fişi" butonu/modalı kaldırıldı; defter/stok düzeyinde FulfillFromStock ile
    /// birebir aynı olduğu için serbest kaynak-depo seçme yeteneği FulfillFromStock'a opsiyonel
    /// OverrideLocationId parametresi olarak taşındı (bkz. FulfillFromStockRequest XML doc'u).
    /// Bu endpoint'i çağıran istemci kalmadı (grep ile doğrulandı) — YETİM ama kaldırma kararı
    /// ayrı, burada silinmedi.
    /// </summary>
    [HttpPost("/Purchase/CreateStockIssue")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.StockOut)]
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

            // Fulfillment takibi: FulfilledFromStock artır (Madde 2 — bkz. CreateTransfer'daki
            // gerekçe: entries belge kaydından ÖNCE hazırlanır, SaveAsync'e verilir, repo
            // belgenin kendi transaction'ı İÇİNDE yazar).
            var linesWithTracking = validLines
                .Where(l => l.RequestLineId.HasValue && l.RequestLineId.Value > 0)
                .GroupBy(l => l.RequestLineId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Qty));

            var allLines = new Dictionary<int, CalibraHub.Application.Contracts.DocumentLineDto>();
            List<PendingFulfillmentEntry>? pendingEntries = null;
            if (linesWithTracking.Count > 0 && req.RequestIds?.Count > 0)
            {
                foreach (var rid in req.RequestIds)
                {
                    var lines2 = await _documentService.GetQuoteLinesAsync(rid, ct);
                    foreach (var l in lines2) allLines[l.Id] = l;
                }
                // allLines geçerlilik filtresi (bkz. CreateTransfer'daki gerekçe).
                pendingEntries = linesWithTracking
                    .Where(kv => allLines.ContainsKey(kv.Key))
                    .Select(kv => new PendingFulfillmentEntry(kv.Key, FulfillmentSourceKind.StockIssue, kv.Value))
                    .ToList();
            }

            // STOCK_DOC_CONSOLIDATE — aç/kapa parametre (varsayılan kapalı = mevcut davranış).
            var consolidate = await _companyParams.GetBoolAsync(
                FulfillmentParameters.FormCode, FulfillmentParameters.ConsolidateLinesKey, ct) ?? false;

            var saveReq = new SaveStockDocRequest(
                Id:             null,
                DocType:        "STOCK_OUT",
                DocNo:          null,
                DocDate:        DateTime.Today,
                FromLocationId: null,
                ToLocationId:   null,
                RefNo:          refNo,
                Notes:          req.Notes,
                Lines:          BuildStockDocLines(validLines.Select(l => new ConsolidationInputLine(
                    l.ItemId, l.UnitId, l.Qty, l.FromLocationId, null, l.CombinationId, l.Notes, l.RequestLineId)),
                    consolidate),
                ArgeProjectId:  null
            );

            var (newDocId, docNo) = await _stockDocRepo.SaveAsync(saveReq, CurrentUserId(), ct, pendingEntries);

            // Belge soyağacı: ambar çıkış fişi ← kaynak İhtiyaç belge(ler)i
            await LinkFulfillmentSourcesAsync(newDocId, req.RequestIds, ct);

            // İşlem logu (Madde 3, 2026-07-20) — bkz. CreateTransfer'daki gerekçe.
            if (allLines.Count > 0)
                await _documentService.LogFulfillmentAuditAsync(allLines, $"Ambar çıkışı #{docNo}", ct);

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
            // Sessizce yutma (CLAUDE.md kural #2) — kumule SQL truncation dahil beklenmeyen hatalar
            // teshis edilebilsin diye logla, istemciye jenerik don.
            _logger.LogError(ex, "[Purchase] Karsilama aksiyonu sirasinda beklenmeyen hata.");
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // ── Depodan Karşıla ──────────────────────────────────────────────────────

    // PageComment Seq 34 (2026-07-25, Admin → Parametreler → İhtiyaç Kayıtları): "Karşılama Deposu
    // Seçimi" listesinde Raf (SHELF) / Hücre (BIN) tipli lokasyonlar seçilebilir OLMAMALI — yalnız
    // depo seviyesi (FACTORY/SECTION veya admin'in LogisticsConfigurationService.CreateLocationTypeAsync
    // ile eklediği özel tip) lokasyonlar. Gerçek tip kodları CalibraDatabaseInitializer LocationType
    // seed'inden doğrulandı (FACTORY/SECTION/SHELF/BIN). Bu filtre YALNIZ bu parametrenin seçim
    // listesi içindir — genel /Warehouse/GetLocationsJson (stok belgesi satırı lokasyon seçimi;
    // orada raf/hücre GEÇERLİ bir seçimdir, üstelik "leaf-only" kuralıyla depo seviyesini zaten
    // hariç tutar) kasıtlı olarak DOKUNULMADI.
    private static readonly HashSet<string> FulfillmentExcludedLocationTypeCodes =
        new(StringComparer.OrdinalIgnoreCase) { "SHELF", "BIN" };

    private static bool IsFulfillmentExcludedLocationType(string? locationTypeCode) =>
        !string.IsNullOrWhiteSpace(locationTypeCode) &&
        FulfillmentExcludedLocationTypeCodes.Contains(locationTypeCode.Trim());

    /// <summary>
    /// "Karşılama Deposu Seçimi" (Admin → Parametreler → İhtiyaç Kayıtları) için seçilebilir depo
    /// listesi. Raf/Hücre tipli lokasyonlar hariç tutulur (yukarı bkz.). /Warehouse/GetLocationsJson'ın
    /// aksine "leaf-only" kısıtı YOKTUR — depo seviyesi lokasyonun altında raf/hücre kırılımı olması
    /// normaldir (aslında tam olarak bu yüzden GetLocationsJson'da depo hiç görünmüyordu, yalnız
    /// altındaki raf/hücre'ler görünüyordu), bu listeden düşürülmemelidir.
    /// GET /Purchase/FulfillmentLocationOptions
    /// </summary>
    [HttpGet("/Purchase/FulfillmentLocationOptions")]
    public async Task<IActionResult> FulfillmentLocationOptions(CancellationToken ct)
    {
        var locations = await _logisticsService.GetLocationsAsync(ct);
        var options = locations
            .Where(l => l.IsActive && !IsFulfillmentExcludedLocationType(l.LocationTypeCode))
            .OrderBy(l => l.SortOrder).ThenBy(l => l.LocationName ?? l.LocationCode)
            .Select(l => new { l.Id, l.LocationCode, l.LocationName })
            .ToList();
        return Json(options);
    }

    /// <summary>
    /// Mevcut şirket parametrelerinden Depodan Karşıla konfigürasyonunu döner.
    /// GET /Purchase/FulfillmentLocationConfig
    /// invalidLocations (PageComment Seq 34): kayıtlı FULFILLMENT_LOCATION_IDS içinde artık geçersiz
    /// (raf/hücre tipine ait, pasifleşmiş veya silinmiş) id varsa burada listelenir — kayıtlı DEĞER
    /// SESSİZCE DEĞİŞTİRİLMEZ veya üst depoya map EDİLMEZ (silent migration riskli); yalnızca UI'ın
    /// uyarı gösterebilmesi içindir. Temizlik, admin'in seçimi elle güncelleyip kaydetmesiyle olur.
    /// </summary>
    [HttpGet("/Purchase/FulfillmentLocationConfig")]
    public async Task<IActionResult> FulfillmentLocationConfig(CancellationToken ct)
    {
        const string fc = "PURCHASE_FULFILLMENT";
        var mode    = await _companyParams.GetStringAsync(fc, FulfillmentParameters.LocationModeKey, ct) ?? "SPECIFIC";
        var idsRaw  = await _companyParams.GetStringAsync(fc, FulfillmentParameters.LocationIdsKey,  ct) ?? "";
        var ids     = idsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim()).Where(s => int.TryParse(s, out _))
                            .Select(int.Parse).ToList();
        var respectMinStock = await _companyParams.GetBoolAsync(fc, FulfillmentParameters.RespectMinStockKey, ct) ?? false;
        var consolidateLines = await _companyParams.GetBoolAsync(fc, FulfillmentParameters.ConsolidateLinesKey, ct) ?? false;

        var invalidLocations = new List<object>();
        if (ids.Count > 0)
        {
            var locations = await _logisticsService.GetLocationsAsync(ct);
            var byId = locations.ToDictionary(l => l.Id);
            foreach (var id in ids)
            {
                var isKnown  = byId.TryGetValue(id, out var loc);
                var isInvalid = !isKnown || !loc!.IsActive || IsFulfillmentExcludedLocationType(loc.LocationTypeCode);
                if (isInvalid)
                    invalidLocations.Add(new { id, label = isKnown ? (loc!.LocationName ?? loc.LocationCode) : $"#{id}" });
            }
        }

        return Json(new { mode, locationIds = ids, respectMinStock, consolidateLines, invalidLocations });
    }

    /// <summary>
    /// "Karşılama Deposu Seçimi" (mode + location id listesi) kaydı. Genel /Admin/SaveParameter
    /// (ParametersController, DOKUNULMADI) yerine bu iki parametreye (FULFILLMENT_LOCATION_MODE/IDS)
    /// özel bir uç: SPECIFIC modda gönderilen id listesi sunucu tarafında raf/hücre tipine ve
    /// aktiflik durumuna karşı doğrulanır — geçersiz id varsa TÜM istek reddedilir (sessiz filtre/
    /// atlama YOK, CLAUDE.md kural #3). Doğrulama sonrası aynı ICompanyParameterService.SetAsync
    /// (genel altyapı) çağrılır — yalnız ek bir doğrulama katmanı eklenir, depolama mekanizması
    /// değişmez. Yetki: eski /Admin/SaveParameter yolu ParametersController üzerinden CompanySettings
    /// scope'una tabiydi; aynı korumayı burada da uyguluyoruz (aksi halde bu taşıma yetkiyi gevşetirdi).
    /// PageComment Seq 34 (2026-07-25).
    /// POST /Purchase/SaveFulfillmentLocationConfig
    /// </summary>
    [HttpPost("/Purchase/SaveFulfillmentLocationConfig")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.CompanySettings)]
    public async Task<IActionResult> SaveFulfillmentLocationConfig(
        [FromBody] SaveFulfillmentLocationConfigRequest req, CancellationToken ct)
    {
        try
        {
            var mode = string.Equals(req?.Mode, "ITEM_DEFAULT", StringComparison.OrdinalIgnoreCase)
                ? "ITEM_DEFAULT" : "SPECIFIC";
            var ids = (req?.LocationIds ?? new List<int>()).Where(id => id > 0).Distinct().ToList();

            if (mode == "SPECIFIC" && ids.Count > 0)
            {
                var locations = await _logisticsService.GetLocationsAsync(ct);
                var byId = locations.ToDictionary(l => l.Id);
                var invalidLabels = ids
                    .Where(id => !byId.TryGetValue(id, out var loc) || !loc.IsActive || IsFulfillmentExcludedLocationType(loc.LocationTypeCode))
                    .Select(id => byId.TryGetValue(id, out var l) ? (l.LocationName ?? l.LocationCode) : $"#{id}")
                    .ToList();
                if (invalidLabels.Count > 0)
                {
                    return Json(new
                    {
                        ok = false,
                        error = "Raf/hücre tipinde veya artık geçersiz lokasyon seçilemez: "
                            + string.Join(", ", invalidLabels) + ". Yalnızca depo seviyesi lokasyonlar seçilebilir.",
                    });
                }
            }

            const string fc = "PURCHASE_FULFILLMENT";
            await _companyParams.SetAsync(
                new SetCompanyParameterRequest(fc, FulfillmentParameters.LocationModeKey, mode, CompanyParameterDataType.String), ct);
            await _companyParams.SetAsync(
                new SetCompanyParameterRequest(fc, FulfillmentParameters.LocationIdsKey, string.Join(",", ids), CompanyParameterDataType.String), ct);

            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            // Sessizce yutma (CLAUDE.md kural #2) — server'a logla, istemciye jenerik mesaj dön.
            _logger.LogError(ex, "[Purchase] Karsilama deposu parametresi kaydedilirken hata.");
            return Json(new { ok = false, error = "Kaydetme sırasında bir hata oluştu." });
        }
    }

    /// <summary>
    /// Depodan Karşıla (2026-07-21 iş kuralı revizyonu — eski FIFO/bakiye dağıtımı KALDIRILDI).
    /// Seçili her ihtiyaç kalemi için: karşılama deposu (parametre — SPECIFIC modda
    /// FULFILLMENT_LOCATION_IDS, ITEM_DEFAULT modda malzemenin ItemLocation.IsDefault deposu)
    /// ile ihtiyaç kaydının deposu (satır DocumentLine.LocationId, boşsa belge başlığı
    /// Document.LocationId — "Hedef Lokasyon") AYNIYSA o depodan Ambar Çıkış Fişi (depo_cikis)
    /// oluşturulur; farklıysa kalem karşılanmaz (matched=false + reason — kullanıcı ayrıca
    /// Depo Transferi kullanır, bu endpoint onu tetiklemez). Kıyaslama STOK BAKİYESİ/KARTI
    /// ÜZERİNDEN DEĞİL yalnızca depo Id eşleşmesi üzerindendir (Location.Id ile Location.LocationCode
    /// birebir - UNIQUE NOT NULL - olduğundan Id karşılaştırması kod karşılaştırmasıyla eşdeğerdir;
    /// ID tabanlı eşleştirme kuralı gereği Id kullanılır, reason metninde kullanıcıya LocationCode
    /// gösterilir). Miktar kullanıcının FulfillmentCenter ortak modalında düzenlediği değerdir
    /// (req.Lines[].Qty) — sunucu "kalan miktar" tavanına otomatik kırpmaz (eski CreateStockIssue
    /// ile aynı davranış); eksi bakiye SaveDirectDocAsync'in kendi NegativeBalanceGuard'ı (parametre
    /// açıksa) ile engellenir.
    /// POST /Purchase/FulfillFromStock
    /// Kayıt yolu eski CreateStockIssue ile AYNI (_stockDocRepo.SaveAsync → SaveDirectDocAsync) —
    /// karşılama defteri dual-write (DocumentLineFulfillment/DocumentLineLink) bu yoldan otomatik
    /// gelir, burada elle dokunulmaz.
    /// Onay: yeni Ambar Çıkış Fişi için aktif bir onay akışı varsa TryAutoStartApprovalAsync
    /// otomatik başlatır (DocumentService.SaveQuoteAsync'teki auto-start ile aynı mantık).
    /// req.OverrideLocationId (2026-07-25, PageComment Seq 26 — FulfillmentCenter'daki ayrı "Ambar
    /// Çıkış Fişi" aksiyonu bu endpoint'le birleştirildi): doluysa yukarıdaki depo-eşleşme
    /// karşılaştırması ve "karşılama deposu tanımlanmamış" kontrolü TAMAMEN atlanır — TÜM seçili
    /// kalemler doğrudan bu depodan (matched=true) karşılanır. Bu dal yalnızca override doluyken
    /// çalışır; override boş/null olduğu her çağrıda aşağıdaki kod AYNEN (değişmemiş) çalışır.
    /// req.CumulateItems (2026-07-25, PageComment Seq 39 — kümüle davranışı modal seviyesinde
    /// opsiyonel yapar): null ise STOCK_DOC_CONSOLIDATE şirket parametresi AYNEN geçerli olur;
    /// true/false gönderilirse yalnız bu karşılama isteği için parametreyi ezer (kalıcı parametre
    /// değişmez). Karşı fişte (STOCK_OUT) fiziksel satırların kümülelenip kümülelenmeyeceğini
    /// belirler — karşılama defteri (DocumentLineFulfillment/pendingEntries) bundan bağımsızdır,
    /// her zaman İhtiyaç satırı (RequestLineId) bazında ayrı yazılır (bkz. aşağıdaki pendingEntries
    /// kurulumu — planned listesinden, BuildStockDocLines'tan ÖNCE ve ondan bağımsız üretilir).
    /// </summary>
    [HttpPost("/Purchase/FulfillFromStock")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseFulfillment)]
    public async Task<IActionResult> FulfillFromStock([FromBody] FulfillFromStockRequest req, CancellationToken ct)
    {
        try
        {
            if (req?.Lines == null || req.Lines.Count == 0)
                return Json(new { ok = false, error = "Kalem seçilmedi." });

            // Aynı LineId birden fazla kez gönderilmişse miktarları topla (savunma amaçlı).
            var qtyByLineId = req.Lines
                .Where(l => l.LineId > 0)
                .GroupBy(l => l.LineId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
            if (qtyByLineId.Count == 0)
                return Json(new { ok = false, error = "Kalem seçilmedi." });

            // 2026-07-25 (PageComment Seq 26 birleşimi): opsiyonel kaynak depo override'ı — dolu ise
            // aşağıdaki parametre-tabanlı eşleşme zorunluluğu bu çağrı için uygulanmaz (per-line
            // loop'taki `if (overrideLocationId.HasValue)` dalına bkz.). Boş/0/null ise mevcut
            // otomatik davranış AYNEN çalışır.
            var overrideLocationId = req.OverrideLocationId is > 0 ? req.OverrideLocationId : null;

            const string fc = "PURCHASE_FULFILLMENT";
            var mode       = await _companyParams.GetStringAsync(fc, "FULFILLMENT_LOCATION_MODE", ct) ?? "SPECIFIC";
            var idsRaw     = await _companyParams.GetStringAsync(fc, "FULFILLMENT_LOCATION_IDS",  ct) ?? "";
            var isSpecific = string.Equals(mode, "SPECIFIC", StringComparison.OrdinalIgnoreCase);

            var configuredLocIds = idsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                         .Select(x => x.Trim()).Where(x => int.TryParse(x, out _))
                                         .Select(int.Parse).ToList();
            if (overrideLocationId == null && isSpecific && configuredLocIds.Count == 0)
                return Json(new { ok = false, error = "Karşılama deposu tanımlanmamış. Şirket Ayarları → Satın Alma bölümünden depo seçin." });

            var s         = _schema.Replace("]", "]]");
            var lineIds   = qtyByLineId.Keys.ToList();
            var paramList = string.Join(",", lineIds.Select((_, i) => $"@l{i}"));

            // -- Seçili satırları + talep deposunu yükle. Talep deposu: satır LocationId,
            //    boşsa belge başlığı LocationId'ye (Hedef Lokasyon) düşer. --
            var lines = new List<(int LineId, int DocId, int ItemId, int? UnitId, int? CombinationId,
                                   int? ReqLocationId, string? ReqLocationCode, string? ReqLocationName)>();
            await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"""
                    SELECT dl.[Id], dl.[DocumentId], dl.[ItemId], dl.[UnitId], dl.[CombinationId],
                           reqLoc.[Id], reqLoc.[LocationCode], reqLoc.[LocationName]
                    FROM [{s}].[DocumentLine] dl
                    INNER JOIN [{s}].[Document] d ON d.[Id] = dl.[DocumentId]
                    LEFT JOIN [{s}].[Location] reqLoc ON reqLoc.[Id] = COALESCE(dl.[LocationId], d.[LocationId])
                    WHERE dl.[Id] IN ({paramList});
                    """;
                for (var i = 0; i < lineIds.Count; i++)
                    cmd.Parameters.Add(new SqlParameter($"@l{i}", lineIds[i]));

                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    lines.Add((
                        r.GetInt32(0), r.GetInt32(1), r.GetInt32(2),
                        r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                        r.IsDBNull(4) ? (int?)null : r.GetInt32(4),
                        r.IsDBNull(5) ? (int?)null : r.GetInt32(5),
                        r.IsDBNull(6) ? null : r.GetString(6),
                        r.IsDBNull(7) ? null : r.GetString(7)));
                }
            }

            if (lines.Count == 0)
                return Json(new { ok = false, error = "Seçili kalemler bulunamadı." });

            var guardError = await CheckFulfillmentApprovalGuardAsync(lines.Select(l => l.DocId), ct);
            if (guardError != null)
                return Json(new { ok = false, error = guardError });

            // ITEM_DEFAULT modunda her malzemenin varsayılan deposunu çöz (FulfillmentLocationConfig
            // ile aynı kaynak: ItemLocation.IsDefault). Override doluyken bu çözüm hiç kullanılmaz
            // (per-line loop'ta atlanır) — gereksiz sorgudan kaçınmak için burada da atlanır.
            var itemDefaultLoc = new Dictionary<int, int>(); // itemId → locationId
            if (overrideLocationId == null && !isSpecific)
            {
                var distinctItemIds = lines.Select(l => l.ItemId).Distinct().ToList();
                var iParamList = string.Join(",", distinctItemIds.Select((_, i) => $"@i{i}"));
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

            // Karşılama deposu kod etiketleri (mismatch mesajı için) — SPECIFIC: configuredLocIds,
            // ITEM_DEFAULT: çözülen varsayılan depoların kümesi. Override doluyken mismatch mesajı
            // hiç üretilmez (per-line loop'ta atlanır) — gereksiz sorgudan kaçınmak için boş liste.
            var fulfillLocIdsToLabel = overrideLocationId != null ? new List<int>()
                : isSpecific ? configuredLocIds : itemDefaultLoc.Values.Distinct().ToList();
            var fulfillLocCodes = new Dictionary<int, string>();
            if (fulfillLocIdsToLabel.Count > 0)
            {
                var lParamList = string.Join(",", fulfillLocIdsToLabel.Select((_, i) => $"@fl{i}"));
                await using var cmdLoc = conn.CreateCommand();
                cmdLoc.CommandText = $"SELECT [Id], [LocationCode] FROM [{s}].[Location] WHERE [Id] IN ({lParamList});";
                for (var i = 0; i < fulfillLocIdsToLabel.Count; i++)
                    cmdLoc.Parameters.Add(new SqlParameter($"@fl{i}", fulfillLocIdsToLabel[i]));
                await using var rLoc = await cmdLoc.ExecuteReaderAsync(ct);
                while (await rLoc.ReadAsync(ct))
                    fulfillLocCodes[rLoc.GetInt32(0)] = rLoc.IsDBNull(1) ? $"#{rLoc.GetInt32(0)}" : rLoc.GetString(1);
            }

            var planned = new List<StockIssueLineRequest>();
            var results = new List<object>();

            // Bulunamayan LineId'ler (geçersiz/stale) — sessizce atlamak yerine açıkça bildir.
            var foundLineIds = lines.Select(l => l.LineId).ToHashSet();
            foreach (var missingId in lineIds.Where(id => !foundLineIds.Contains(id)))
                results.Add(new { lineId = missingId, matched = false, fulfilled = 0m, reason = "Kalem bulunamadı." });

            // -- Depo eşleşmesi: kalem bazında karar (stok bakiyesi sorgulanmaz) --
            foreach (var line in lines)
            {
                var qty = qtyByLineId.GetValueOrDefault(line.LineId);
                if (qty <= 0)
                {
                    results.Add(new { lineId = line.LineId, matched = false, fulfilled = 0m, reason = "Miktar sıfır veya negatif olamaz." });
                    continue;
                }

                // Override modu (2026-07-25, PageComment Seq 26): kullanıcı kaynak depoyu bilinçli
                // seçti — aşağıdaki talep-deposu / karşılama-deposu eşleşme kontrolü BU KALEM için
                // uygulanmaz (eski CreateStockIssue/"Ambar Çıkış Fişi" davranışının birebir
                // karşılığı; o akış da talep deposuna hiç bakmıyordu). Yalnızca override doluyken
                // devreye girer, aşağıdaki AYNEN-korunan kod dalına hiç girmez (early continue).
                if (overrideLocationId.HasValue)
                {
                    planned.Add(new StockIssueLineRequest(
                        ItemId:         line.ItemId,
                        UnitId:         line.UnitId,
                        Qty:            qty,
                        FromLocationId: overrideLocationId.Value,
                        CombinationId:  line.CombinationId,
                        Notes:          null,
                        RequestLineId:  line.LineId));
                    results.Add(new { lineId = line.LineId, matched = true, fulfilled = qty, reason = (string?)null });
                    continue;
                }

                if (!line.ReqLocationId.HasValue)
                {
                    results.Add(new { lineId = line.LineId, matched = false, fulfilled = 0m, reason = "İhtiyaç kaydında depo belirtilmemiş." });
                    continue;
                }

                bool matched;
                string fulfillLabel;
                if (isSpecific)
                {
                    matched      = configuredLocIds.Contains(line.ReqLocationId.Value);
                    fulfillLabel = string.Join(", ", configuredLocIds.Select(id => fulfillLocCodes.GetValueOrDefault(id, $"#{id}")));
                }
                else if (itemDefaultLoc.TryGetValue(line.ItemId, out var defLoc))
                {
                    matched      = defLoc == line.ReqLocationId.Value;
                    fulfillLabel = fulfillLocCodes.GetValueOrDefault(defLoc, $"#{defLoc}");
                }
                else
                {
                    results.Add(new { lineId = line.LineId, matched = false, fulfilled = 0m, reason = "Malzemenin varsayılan deposu tanımlı değil." });
                    continue;
                }

                if (!matched)
                {
                    var reqLabel = line.ReqLocationCode ?? line.ReqLocationName ?? $"#{line.ReqLocationId}";
                    results.Add(new
                    {
                        lineId    = line.LineId,
                        matched   = false,
                        fulfilled = 0m,
                        reason    = $"Talep deposu ({reqLabel}) karşılama deposundan ({fulfillLabel}) farklı.",
                    });
                    continue;
                }

                planned.Add(new StockIssueLineRequest(
                    ItemId:         line.ItemId,
                    UnitId:         line.UnitId,
                    Qty:            qty,
                    FromLocationId: line.ReqLocationId.Value,
                    CombinationId:  line.CombinationId,
                    Notes:          null,
                    RequestLineId:  line.LineId));
                results.Add(new { lineId = line.LineId, matched = true, fulfilled = qty, reason = (string?)null });
            }

            if (planned.Count == 0)
                return Json(new { ok = true, docNo = (string?)null, approvalStarted = false, results });

            // -- Ambar Çıkış Fişi oluştur (CreateStockIssue ile AYNI kayıt yolu) + FulfilledFromStock güncelle --
            var docIds = lines.Select(l => l.DocId).Distinct().ToList();
            string? refNo = null;
            foreach (var docId in docIds)
            {
                var doc = await _documentService.GetQuoteByIdAsync(docId, ct);
                if (doc != null) refNo = (refNo == null ? "" : refNo + ", ") + doc.DocumentNumber;
            }

            var allLines = new Dictionary<int, DocumentLineDto>();
            foreach (var docId in docIds)
            {
                var lines3 = await _documentService.GetQuoteLinesAsync(docId, ct);
                foreach (var l in lines3) allLines[l.Id] = l;
            }

            var byLineId = planned.GroupBy(l => l.RequestLineId!.Value)
                                  .ToDictionary(g => g.Key, g => g.Sum(l => l.Qty));
            var pendingEntries = byLineId
                .Select(kv => new PendingFulfillmentEntry(kv.Key, FulfillmentSourceKind.StockIssue, kv.Value))
                .ToList();

            // STOCK_DOC_CONSOLIDATE — şirket parametresi varsayılanı belirler; FulfillmentCenter
            // "Depodan Karşıla" modalındaki "Stokları Kümüle Et" switch'i (PageComment Seq 39,
            // 2026-07-25) req.CumulateItems doluysa BU TEK istek için parametreyi ezer (kalıcı
            // parametre değişmez). Switch'e dokunulmadıysa (null) mevcut parametre-tabanlı davranış
            // AYNEN çalışır — geriye dönük uyum.
            var consolidate = req.CumulateItems ?? await _companyParams.GetBoolAsync(
                FulfillmentParameters.FormCode, FulfillmentParameters.ConsolidateLinesKey, ct) ?? false;

            var saveReq = new SaveStockDocRequest(
                Id:             null,
                DocType:        "STOCK_OUT",
                DocNo:          null,
                DocDate:        DateTime.Today,
                FromLocationId: null,
                ToLocationId:   null,
                RefNo:          refNo,
                Notes:          req.Notes,
                Lines:          BuildStockDocLines(planned.Select(l => new ConsolidationInputLine(
                                    l.ItemId, l.UnitId, l.Qty, l.FromLocationId, null, l.CombinationId, l.Notes, l.RequestLineId)),
                                    consolidate),
                ArgeProjectId:  null);

            var (newDocId, docNo) = await _stockDocRepo.SaveAsync(saveReq, CurrentUserId(), ct, pendingEntries);

            // Belge soyağacı: Ambar Çıkış Fişi ← kaynak İhtiyaç belge(ler)i
            await LinkFulfillmentSourcesAsync(newDocId, docIds, ct);

            // Onay: depo_cikis için aktif akış varsa otomatik başlat (bkz. metot/helper XML doc'u).
            var approvalStarted = await TryAutoStartApprovalAsync(newDocId, "depo_cikis", ct);

            // İşlem logu (Madde 3, 2026-07-20) — bkz. CreateTransfer'daki gerekçe.
            if (allLines.Count > 0)
                await _documentService.LogFulfillmentAuditAsync(allLines, $"Depodan karşılama #{docNo}", ct);

            return Json(new { ok = true, docNo, approvalStarted, results });
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
            _logger.LogError(ex, "FulfillFromStock: Depodan karşılama sırasında beklenmeyen hata.");
            return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." });
        }
    }

    /// <summary>
    /// Satın alma talebi oluşturur (alis_siparisi belgesi) ve ihtiyaç satırlarının FulfilledByPurchase günceller.
    /// POST /Purchase/CreatePurchaseOrderFromIhtiyac
    /// </summary>
    [HttpPost("/Purchase/CreatePurchaseOrderFromIhtiyac")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseOrder)]
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

            // Fulfillment takibi: FulfilledByPurchase artır (Madde 2 — entries belge
            // kaydından ÖNCE hazırlanır, SaveQuoteAsync'e verilir; repo kalem yazımıyla AYNI
            // transaction'da deftere yazar — bkz. CreateTransfer'daki gerekçe).
            var linesWithTracking = validLines
                .Where(l => l.RequestLineId.HasValue && l.RequestLineId.Value > 0)
                .GroupBy(l => l.RequestLineId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Qty));

            var allLines = new Dictionary<int, CalibraHub.Application.Contracts.DocumentLineDto>();
            List<PendingFulfillmentEntry>? pendingEntries = null;
            if (linesWithTracking.Count > 0 && req.RequestIds?.Count > 0)
            {
                foreach (var rid in req.RequestIds)
                {
                    var lines2 = await _documentService.GetQuoteLinesAsync(rid, ct);
                    foreach (var l in lines2) allLines[l.Id] = l;
                }
                // allLines geçerlilik filtresi (bkz. CreateTransfer'daki gerekçe).
                pendingEntries = linesWithTracking
                    .Where(kv => allLines.ContainsKey(kv.Key))
                    .Select(kv => new PendingFulfillmentEntry(kv.Key, FulfillmentSourceKind.PurchaseOrder, kv.Value))
                    .ToList();
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

            var (success, error, doc, _) = await _documentService.SaveQuoteAsync(
                saveDocReq, CurrentUserId(), User?.Identity?.Name, ct, pendingEntries);
            if (!success || doc == null)
                return Json(new { ok = false, error = error ?? "Belge oluşturulamadı." });

            // Belge soyağacı: sipariş ← TÜM kaynak İhtiyaç belgeleri (SaveQuoteAsync yalnız
            // FromRequestId=ilkini bağlar; çoklu kaynak için hepsini idempotent ekle).
            await LinkFulfillmentSourcesAsync(doc.Id, req.RequestIds, ct);

            // İşlem logu (Madde 3, 2026-07-20) — bkz. CreateTransfer'daki gerekçe.
            if (allLines.Count > 0)
                await _documentService.LogFulfillmentAuditAsync(allLines, $"Satın alma siparişi #{doc.DocumentNumber}", ct);

            return Json(new { ok = true, docNo = doc.DocumentNumber, docId = doc.Id });
        }
        catch (Exception ex)
        {
            // Sessizce yutma (CLAUDE.md kural #2) — kumule SQL truncation dahil beklenmeyen hatalar
            // teshis edilebilsin diye logla, istemciye jenerik don.
            _logger.LogError(ex, "[Purchase] Karsilama aksiyonu sirasinda beklenmeyen hata.");
            return Json(new { ok = false, error = "Islem sirasinda bir hata olustu." });
        }
    }

    // NOT (2026-07-22, kullanıcı kararı): Eski "POST /Purchase/CloseRequests" ucu (belgenin
    // TAMAMINI Cancelled yapan sweep) KALDIRILDI — UI'dan çağıranı kalmamıştı (Seq 12 tek-buton
    // birleşimi) ve belge iptali zaten belge ekranındaki durum değiştirme akışında mevcut.
    // Satır bazlı kapatma ihtiyacının tek ucu aşağıdaki CloseFulfillmentLines'tır.

    /// <summary>
    /// FulfillmentCenter — TEK "İhtiyacı Kapat" aksiyonu (2026-07-21, PageComment Seq 12).
    /// Seçili İhtiyaç KALEMLERİNİN karşılanmamış kalan miktarını kapatır.
    /// POST /Purchase/CloseFulfillmentLines
    ///
    /// Bu uç yalnız verilen satırları etkiler (FulfillmentStatus = 3), karşılanan
    /// miktarları korur — "bu kalem için artık bir şey yapmayacağız" anlamındadır.
    /// (Belgenin tamamını iptal eden eski CloseRequests sweep'i 2026-07-22'de kaldırıldı;
    /// belge iptali için belge ekranındaki durum değiştirme akışı kullanılır.)
    ///
    /// Hem "hiç karşılama yapılmadı" hem "kısmen karşılandı" senaryosunu AYNI koşulsuz
    /// UPDATE ile kapsar: FulfilledFromStock/FulfilledByPurchase koşulsuz korunur (0 da
    /// olsa, kısmi bir değer de olsa dokunulmaz), yalnızca FulfillmentStatus=3 yazılır. Bu
    /// yüzden hiç karşılanmamış bir kalemde "kalan miktar" zaten miktarın tamamı olduğundan
    /// ayrı bir "tam kapat" dalı GEREKMEZ — FulfillmentCenter ekranı artık tek "İhtiyacı
    /// Kapat" butonuyla yalnız bu uca bağlanıyor.
    ///
    /// Tipik senaryo: bir ihtiyaç kaleminin bir kısmı depodan/transferle, bir kısmı
    /// satın alma talebiyle karşılanır; geri kalanından vazgeçilip kalem kapatılır. Karşılama
    /// defterine (DocumentLineFulfillment) kayıt YAZILMAZ — kapatma bir karşılama kaynağı
    /// değildir, defterdeki mevcut aktif kayıtlar (ve onlardan türetilen toplamlar) aynen
    /// korunur.
    /// </summary>
    [HttpPost("/Purchase/CloseFulfillmentLines")]
    [ValidateAntiForgeryToken]
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseFulfillment)]
    public async Task<IActionResult> CloseFulfillmentLines(
        [FromBody] CloseFulfillmentLinesRequest req, CancellationToken ct)
    {
        if (req?.LineIds == null || req.LineIds.Count == 0)
            return Json(new { ok = false, error = "Kapatılacak kalem seçilmedi." });

        try
        {
            // Repo tarafı UPDATE'i yalnız "alis_talebi" satırlarıyla sınırlar; başka belge
            // tipinin satırı gönderilse bile etkilenmez (closed < gönderilen sayı olur).
            var closed = await _documentRepo.CloseLineFulfillmentAsync(req.LineIds, ct);
            return Json(new { ok = true, closed });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Fulfillment.CloseLines] Kalem kapatma başarısız. LineIds: {LineIds}", string.Join(",", req.LineIds));
            return Json(new { ok = false, error = "Kalemler kapatılamadı." });
        }
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
                i.[Code]              AS materialCode,
                i.[Name]              AS materialName,
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
            LEFT  JOIN [{s}].[Unit]           mu  ON mu.[Id] = dl.[UnitId]
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
              AND (@MatSearch IS NULL OR i.[Code] LIKE @MatSearch OR i.[Name] LIKE @MatSearch)
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
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseDemand)]
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
            // GEÇERLİLİK FİLTRESİ (zorunlu): lineIds doğrudan istemciden gelir. Belge tipi
            // kısıtlanmazsa kullanıcı buraya bir satış siparişinin satır Id'sini gönderip o
            // satırın FulfilledByPurchase'ını artırabilir — ve o belge "karşılanmış kalem
            // içeriyor" guard'ı yüzünden bir daha silinemez hale gelir. Yani düzeltilen
            // "sonsuza dek kilitli belge" hatası başka bir kapıdan üretilebilirdi.
            // Yalnız İhtiyaç Kaydı (alis_talebi) satırları karşılanabilir.
            cmdFetch.CommandText = $"""
                SELECT dl.[Id], dl.[DocumentId], dl.[ItemId], dl.[UnitId], dl.[Quantity],
                       ISNULL(dl.[FulfilledByPurchase],0), ISNULL(dl.[FulfilledFromStock],0),
                       dl.[CombinationId], dl.[Notes]
                FROM [{s}].[DocumentLine] dl
                INNER JOIN [{s}].[Document] d      ON d.[Id]  = dl.[DocumentId]
                INNER JOIN [{s}].[DocumentType] dt ON dt.[Id] = d.[DocumentTypeId]
                WHERE dl.[Id] IN ({paramList})
                  AND d.[IsActive] = 1
                  AND dt.[Code] = 'alis_talebi';
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

            // İşlem logu (Madde 3) için mutasyondan ÖNCEki satır snapshot'ı — lineRows ham SQL
            // olduğu için MaterialName/FulfillmentStatus taşımıyor; DTO'yu ayrıca çekeriz
            // (bkz. CreateTransfer'daki gerekçe — diğer 4 yazım noktasıyla aynı desen).
            var allLines = new Dictionary<int, DocumentLineDto>();
            foreach (var rid in sourceDocIds)
            {
                var lines4 = await _documentService.GetQuoteLinesAsync(rid, ct);
                foreach (var l in lines4) allLines[l.Id] = l;
            }

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

            // Madde 2: entries belge kaydından ÖNCE hazırlanır (bkz. CreateTransfer'daki
            // gerekçe). demandLines zaten İhtiyaç satırlarından türediği için (RequestLineId
            // = lr.Id) ayrıca bir allLines doğrulamasına gerek yok — kaynak sorgu zaten yalnız
            // 'alis_talebi' satırlarını döner (yukarıda cmdFetch). Diğer kova (FulfilledFromStock)
            // artık ezilemez — toplamlar defterden türetildiği için korunması otomatiktir.
            var pendingEntries = demandLines
                .Select(dl => new PendingFulfillmentEntry(dl.LineId, FulfillmentSourceKind.PurchaseDemand, dl.Qty))
                .ToList();

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

            var (success, error, doc, _) = await _documentService.SaveQuoteAsync(
                saveReq, CurrentUserId(), User?.Identity?.Name, ct, pendingEntries);
            if (!success || doc == null)
                return Json(new { ok = false, error = error ?? "Belge oluşturulamadı." });

            // DocumentSource bağlantıları — her kaynak belge için.
            // Yön: AddAsync(türetilen=talep doc.Id, kaynak=İhtiyaç srcId). (2026-07-08 yön düzeltmesi)
            foreach (var srcId in sourceDocIds)
                await _docSourceRepo.AddAsync(doc.Id, srcId, ct);

            // İşlem logu (Madde 3, 2026-07-20) — bkz. CreateTransfer'daki gerekçe.
            if (allLines.Count > 0)
                await _documentService.LogFulfillmentAuditAsync(allLines, $"Satın alma talebi #{doc.DocumentNumber}", ct);

            return Json(new { ok = true, docNo = doc.DocumentNumber, docId = doc.Id });
        }
        catch (Exception ex)
        {
            // Sessizce yutma (CLAUDE.md kural #2) — kumule SQL truncation dahil beklenmeyen hatalar
            // teshis edilebilsin diye logla, istemciye jenerik don.
            _logger.LogError(ex, "[Purchase] Karsilama aksiyonu sirasinda beklenmeyen hata.");
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
        catch (Exception ex)
        {
            // Sessizce yutma (CLAUDE.md kural #2) — view/kolon yoksa bos liste donmek dogru
            // davranis (opsiyonel ek alan ozelligi), ama nedeni teshis edilebilsin diye logla.
            _logger.LogWarning(ex, "[Purchase.GetFulfillmentExtraColumnsAsync] cbv_FulfillmentLineExtras kolonlari okunamadi.");
            return [];
        }
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
        catch (Exception ex)
        {
            // FAIL-CLOSED: bos donmek yetki kapisini atlatir (pending onay belgesi yokmus gibi).
            // Logla ve RETHROW et — guard (CheckFulfillmentApprovalGuardAsync) bunu yakalayip
            // karsilamayi engeller; salt-gosterim cagrisi (FulfillmentCenter board) kendi
            // try/catch'inde bos listeye duser. Sessiz bos donus YASAK (CLAUDE.md kural #2).
            _logger.LogError(ex, "[Purchase.GetPendingApprovalDocIdsAsync] Bekleyen onay belge ID'leri okunamadi.");
            throw;
        }
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
    [CalibraHub.Web.Authorization.PermissionScope(FormCodes.PurchaseFulfillment)]
    [CalibraHub.Web.Authorization.PermissionAction("VIEW", "VIEW_OWN")]
    public async Task<IActionResult> SaveFlatColConfig([FromBody] Fc3SaveColConfigRequest request, CancellationToken ct)
    {
        var uid = CurrentUserId();
        if (!uid.HasValue) return Json(new { ok = false });
        await _userSettingRepo.SetAsync(uid.Value, FlatColCfgKey, request.Config, ct);
        return Json(new { ok = true });
    }
}

public sealed record Fc3SaveColConfigRequest(string? Config);
