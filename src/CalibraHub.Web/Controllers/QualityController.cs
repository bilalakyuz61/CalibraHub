using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Kalite Yönetimi — Faz 1: Hata Kodu Kataloğu + Muayene Planı + Muayene Kaydı (NCR).
/// Stok/depoya SIFIR dokunuş. Class-level yetki = QUALITY_INSPECTION_EDIT; katalog/plan
/// action'ları kendi form koduyla override eder (ActivityReason deseni).
/// </summary>
[Authorize]
[PermissionScope(FormCodes.QualityInspectionEdit)]
public sealed class QualityController : Controller
{
    private readonly IQualityService _quality;
    private readonly IQualityDefectCodeService _defectCodes;
    private readonly ICapaService _capa;
    private readonly ILogisticsConfigurationService _logistics;
    private readonly IDocumentRepository _documents;
    private readonly IStockDocRepository _stockDocs;
    private readonly IWorkOrderRepository _workOrders;
    private readonly ILogger<QualityController> _logger;

    public QualityController(IQualityService quality, IQualityDefectCodeService defectCodes,
        ICapaService capa, ILogisticsConfigurationService logistics, IDocumentRepository documents,
        IStockDocRepository stockDocs, IWorkOrderRepository workOrders, ILogger<QualityController> logger)
    {
        _quality = quality;
        _defectCodes = defectCodes;
        _capa = capa;
        _logistics = logistics;
        _documents = documents;
        _stockDocs = stockDocs;
        _workOrders = workOrders;
        _logger = logger;
    }

    // GET /Quality/ItemsLookup → plan/muayene malzeme seçici (id, code, name)
    [HttpGet]
    public async Task<IActionResult> ItemsLookup(CancellationToken ct)
    {
        var items = await _logistics.GetItemsForLookupAsync(ct);
        return Json(items.Where(i => i.IsActive).Select(i => new { id = i.Id, code = i.Code, name = i.Name }));
    }

    // GET /Quality/MaterialGroupsLookup → plan formu malzeme grubu seçici, CardGroup (CardType=1).
    // Döner: [{ id, name }] — name hiyerarşi yolu ("Üst > Alt Kod — Açıklama").
    [HttpGet]
    [PermissionScope(FormCodes.QualityInspectionPlan)]
    public async Task<IActionResult> MaterialGroupsLookup(CancellationToken ct)
    {
        var groups = await _quality.ListMaterialGroupsAsync(ct);
        return Json(groups.Select(g => new { id = g.Id, name = g.Name }));
    }

    private int? CurrentUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    // ════════════════════════════════════════════════════════════════
    // Hata Kodu Kataloğu
    // ════════════════════════════════════════════════════════════════

    [HttpGet]
    [PermissionScope(FormCodes.QualityDefectCode)]
    public async Task<IActionResult> DefectCodes(CancellationToken ct)
    {
        ViewBag.BoardConfig = await BuildDefectCodesBoardAsync(ct);
        return View();
    }

    [HttpGet]
    [PermissionScope(FormCodes.QualityDefectCode)]
    public async Task<IActionResult> DefectCodesBoardConfig(CancellationToken ct)
        => Json(await BuildDefectCodesBoardAsync(ct));

    private async Task<object> BuildDefectCodesBoardAsync(CancellationToken ct)
    {
        var codes = await _defectCodes.ListAsync(null, includeInactive: false, ct);
        var entities = codes.Select(c => new
        {
            id = c.Id,
            title = c.Name,
            subtitle = c.CategoryLabel,
            description = c.Description,
            statusBadge = c.IsActive ? (object)new { label = "Aktif", color = "emerald" } : new { label = "Pasif", color = "slate" },
            widgets = new object[]
            {
                new { id = "w_cat", type = "data", dataType = "options", label = "Kategori", value = c.CategoryLabel, color = "indigo" },
            },
            primaryAction = new { label = "Düzenle", icon = "Edit", color = "amber", url = $"/Quality/DefectCodeEdit?id={c.Id}", hideButton = true },
            secondaryAction = new { label = "Sil", icon = "Trash2", apiUrl = $"/Quality/DeleteDefectCodeJson?id={c.Id}", apiMethod = "POST", confirm = $"Bu hata kodunu silmek istediğinize emin misiniz? ({c.Name})" },
        }).ToArray();
        return new
        {
            boardKey = "quality-defect-codes",
            title = "Hata Kodları",
            subtitle = $"{codes.Count} kod",
            icon = "AlertTriangle",
            iconColor = "rose",
            refreshUrl = "/Quality/DefectCodesBoardConfig",
            searchPlaceholder = "Hızlı ara…",
            emptyText = "Henüz hata kodu tanımlanmamış. Uygunsuz ölçüm satırlarında seçilecek hata kodlarını burada tanımlarsınız.",
            actions = new object[] { new { id = "new", label = "Yeni Hata Kodu", icon = "Plus", variant = "primary", url = "/Quality/DefectCodeEdit" } },
            masterWidgets = new object[] { },
            entities,
        };
    }

    [HttpGet]
    [PermissionScope(FormCodes.QualityDefectCode)]
    public async Task<IActionResult> DefectCodeEdit(int? id, CancellationToken ct)
    {
        QualityDefectCodeDto? dto = null;
        if (id is > 0)
        {
            dto = await _defectCodes.GetAsync(id.Value, ct);
            if (dto is null) return NotFound();
        }
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermissionScope(FormCodes.QualityDefectCode)]
    public async Task<IActionResult> SaveDefectCode([FromBody] SaveQualityDefectCodeRequest? req, CancellationToken ct)
    {
        if (req is null) return Json(new { ok = false, error = "Geçersiz istek." });
        try
        {
            var (ok, error, id) = await _defectCodes.SaveAsync(req, CurrentUserId(), ct);
            return Json(new { ok, error, id });
        }
        catch (Exception ex) { _logger.LogError(ex, "Hata kodu kaydetme hatası (id={Id})", req.Id); return Json(new { ok = false, error = "Kaydedilirken bir hata oluştu." }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermissionScope(FormCodes.QualityDefectCode)]
    public async Task<IActionResult> DeleteDefectCodeJson(int id, CancellationToken ct)
    {
        try { var (ok, error) = await _defectCodes.DeleteAsync(id, CurrentUserId(), ct); return Json(new { ok, error }); }
        catch (Exception ex) { _logger.LogError(ex, "Hata kodu silme hatası (id={Id})", id); return Json(new { ok = false, error = "Silinirken bir hata oluştu." }); }
    }

    // ════════════════════════════════════════════════════════════════
    // Muayene Planı
    // ════════════════════════════════════════════════════════════════

    [HttpGet]
    [PermissionScope(FormCodes.QualityInspectionPlan)]
    public async Task<IActionResult> InspectionPlans(CancellationToken ct)
    {
        ViewBag.BoardConfig = await BuildPlansBoardAsync(ct);
        return View();
    }

    [HttpGet]
    [PermissionScope(FormCodes.QualityInspectionPlan)]
    public async Task<IActionResult> InspectionPlansBoardConfig(CancellationToken ct)
        => Json(await BuildPlansBoardAsync(ct));

    private async Task<object> BuildPlansBoardAsync(CancellationToken ct)
    {
        var plans = await _quality.ListPlansAsync(null, null, ct);
        var entities = plans.Select(p => new
        {
            id = p.Id,
            title = p.Name,
            subtitle = p.InspectionTypeLabel,
            description = p.ItemName ?? p.MaterialGroupName,
            statusBadge = p.IsActive ? (object)new { label = "Aktif", color = "emerald" } : new { label = "Pasif", color = "slate" },
            widgets = new object[]
            {
                new { id = "w_type", type = "data", dataType = "options", label = "Tip", value = p.InspectionTypeLabel, color = "indigo" },
                new { id = "w_lines", type = "data", dataType = "numeric", label = "Karakteristik", value = p.LineCount.ToString(), detail = "adet", color = "slate" },
            },
            primaryAction = new { label = "Düzenle", icon = "Edit", color = "amber", url = $"/Quality/InspectionPlanEdit?id={p.Id}", hideButton = true },
            secondaryAction = new { label = "Sil", icon = "Trash2", apiUrl = $"/Quality/DeletePlanJson?id={p.Id}", apiMethod = "POST", confirm = $"Bu muayene planını silmek istediğinize emin misiniz? ({p.Name})" },
        }).ToArray();
        return new
        {
            boardKey = "quality-inspection-plans",
            title = "Muayene Planları",
            subtitle = $"{plans.Count} plan",
            icon = "ClipboardCheck",
            iconColor = "indigo",
            refreshUrl = "/Quality/InspectionPlansBoardConfig",
            searchPlaceholder = "Hızlı ara… (plan, malzeme)",
            emptyText = "Henüz muayene planı yok. Malzeme × muayene tipi için kontrol edilecek karakteristikleri ve toleransları tanımlayın.",
            actions = new object[] { new { id = "new", label = "Yeni Plan", icon = "Plus", variant = "primary", url = "/Quality/InspectionPlanEdit" } },
            masterWidgets = new object[] { },
            entities,
        };
    }

    [HttpGet]
    [PermissionScope(FormCodes.QualityInspectionPlan)]
    public async Task<IActionResult> InspectionPlanEdit(int? id, CancellationToken ct)
    {
        QualityInspectionPlanDetail? model = id is > 0 ? await _quality.GetPlanAsync(id.Value, ct) : null;
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermissionScope(FormCodes.QualityInspectionPlan)]
    public async Task<IActionResult> SavePlan([FromBody] SaveQualityInspectionPlanRequest? req, CancellationToken ct)
    {
        if (req is null) return Json(new { ok = false, error = "Geçersiz istek." });
        try { var (ok, error, id) = await _quality.SavePlanAsync(req, CurrentUserId(), ct); return Json(new { ok, error, id }); }
        catch (ArgumentException ax) { return Json(new { ok = false, error = ax.Message }); }
        catch (Exception ex) { _logger.LogError(ex, "Muayene planı kaydetme hatası (id={Id})", req.Id); return Json(new { ok = false, error = "Kaydedilirken bir hata oluştu." }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermissionScope(FormCodes.QualityInspectionPlan)]
    public async Task<IActionResult> DeletePlanJson(int id, CancellationToken ct)
    {
        try { var (ok, error) = await _quality.DeletePlanAsync(id, CurrentUserId(), ct); return Json(new { ok, error }); }
        catch (Exception ex) { _logger.LogError(ex, "Muayene planı silme hatası (id={Id})", id); return Json(new { ok = false, error = "Silinirken bir hata oluştu." }); }
    }

    // GET /Quality/PlanForItem?itemId=&inspectionType= → muayene açılışında satır türetme
    [HttpGet]
    public async Task<IActionResult> PlanForItem(int itemId, byte inspectionType, CancellationToken ct)
        => Json(await _quality.FindPlanForItemAsync(itemId, inspectionType, ct));

    // ════════════════════════════════════════════════════════════════
    // Muayene Kaydı
    // ════════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> Inspections(CancellationToken ct)
    {
        ViewBag.BoardConfig = await BuildInspectionsBoardAsync(ct);
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> InspectionsBoardConfig(CancellationToken ct)
        => Json(await BuildInspectionsBoardAsync(ct));

    private async Task<object> BuildInspectionsBoardAsync(CancellationToken ct)
    {
        var items = await _quality.ListInspectionsAsync(null, null, null, ct);
        var entities = items.Select(m =>
        {
            var badge = m.Verdict switch
            {
                1 => (object)new { label = m.VerdictLabel, color = "emerald" },
                2 => new { label = m.VerdictLabel, color = "rose" },
                3 => new { label = m.VerdictLabel, color = "amber" },
                _ => new { label = m.StatusLabel, color = "slate" },
            };
            return new
            {
                id = m.DocumentId,
                title = m.DocumentNumber,
                subtitle = $"{m.InspectionTypeLabel}{(m.ItemName is { Length: > 0 } ? " · " + m.ItemName : "")}",
                description = m.DispositionLabel is { Length: > 0 } ? "Karar: " + m.DispositionLabel : null,
                statusBadge = badge,
                widgets = new object[]
                {
                    new { id = "w_type", type = "data", dataType = "options", label = "Tip", value = m.InspectionTypeLabel, color = "indigo" },
                    new { id = "w_status", type = "data", dataType = "options", label = "Durum", value = m.StatusLabel, color = "slate" },
                },
                primaryAction = new { label = "Aç", icon = "Edit", color = "amber", url = $"/Quality/InspectionEdit?id={m.DocumentId}", hideButton = true },
                secondaryAction = new { label = "Sil", icon = "Trash2", apiUrl = $"/Quality/DeleteInspectionJson?id={m.DocumentId}", apiMethod = "POST", confirm = $"Bu muayene kaydını silmek istediğinize emin misiniz? ({m.DocumentNumber})" },
            };
        }).ToArray();
        return new
        {
            boardKey = "quality-inspections",
            title = "Muayeneler",
            subtitle = $"{items.Count} muayene",
            icon = "ShieldCheck",
            iconColor = "emerald",
            refreshUrl = "/Quality/InspectionsBoardConfig",
            searchPlaceholder = "Hızlı ara… (belge no, malzeme)",
            emptyText = "Henüz muayene kaydı yok. Gelen/proses/final/lab muayenelerini buradan açarsınız.",
            actions = new object[] { new { id = "new", label = "Yeni Muayene", icon = "Plus", variant = "primary", url = "/Quality/InspectionEdit" } },
            masterWidgets = new object[] { },
            entities,
        };
    }

    // sourceKind/sourceId/itemId/quantity: Karşılama Merkezi / İş Emri gibi ekranlardan
    // "Muayene Aç" linkiyle yeni kayıt açılırken ön-doldurma. Var olan kayıt (id dolu) bu
    // parametirleri YOK SAYAR — kayıtlı SourceKind/SourceId zaten QualityInspectionDetail'de gelir.
    [HttpGet]
    public async Task<IActionResult> InspectionEdit(int? id, string? sourceKind, int? sourceId, int? itemId, decimal? quantity, CancellationToken ct)
    {
        QualityInspectionDetail? model = id is > 0 ? await _quality.GetInspectionAsync(id.Value, ct) : null;
        if (model is null)
        {
            // Yeni kayıt — view bu ViewData anahtarlarını okuyup hidden input + preselect için kullanır.
            ViewData["PrefillSourceKind"] = sourceKind;
            ViewData["PrefillSourceId"] = sourceId;
            ViewData["PrefillItemId"] = itemId;
            ViewData["PrefillQuantity"] = quantity;
        }
        return View(model);
    }

    // GET /Quality/SourceDocumentsLookup?kind=alis_irsaliyesi|depo_giris|is_emri&search=
    // → [{ sourceKind, sourceId, label, date }] — muayene formunda kaynak belge arama/seçici.
    [HttpGet]
    public async Task<IActionResult> SourceDocumentsLookup(string? kind, string? search, CancellationToken ct)
    {
        try
        {
            switch (kind)
            {
                case "alis_irsaliyesi":
                {
                    var docs = await _documents.GetByTypeAsync("alis_irsaliyesi", search, null, ct);
                    return Json(docs.Select(d => new
                    {
                        sourceKind = "alis_irsaliyesi",
                        sourceId = d.Id,
                        label = d.DocumentNumber,
                        date = (DateTime?)d.DocumentDate,
                    }));
                }
                case "depo_giris":
                {
                    var docs = await _stockDocs.GetByTypeAsync("depo_giris", ct);
                    var filtered = string.IsNullOrWhiteSpace(search)
                        ? docs
                        : docs.Where(d => d.DocNo.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
                    return Json(filtered.Select(d => new
                    {
                        sourceKind = "depo_giris",
                        sourceId = d.Id,
                        label = d.DocNo,
                        date = (DateTime?)d.DocDate,
                    }));
                }
                case "is_emri":
                {
                    var orders = await _workOrders.ListAsync(null, ct);
                    var filtered = string.IsNullOrWhiteSpace(search)
                        ? orders
                        : orders.Where(w => w.OrderNumber.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
                    return Json(filtered.Select(w => new
                    {
                        sourceKind = "is_emri",
                        sourceId = w.Id, // WorkOrder.Id (PK) — Document.Id DEĞİL.
                        label = w.OrderNumber,
                        date = (DateTime?)w.OrderDate,
                    }));
                }
                default:
                    return Json(Array.Empty<object>());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kaynak belge arama hatası (kind={Kind}, search={Search})", kind, search);
            return Json(Array.Empty<object>());
        }
    }

    // GET /Quality/SourceDocumentLinesLookup?sourceKind=&sourceId=
    // → [{ itemId, itemName, quantity, unitId, unitCode }] — seçilen kaynak belgenin kalemleri.
    [HttpGet]
    public async Task<IActionResult> SourceDocumentLinesLookup(string? sourceKind, int? sourceId, CancellationToken ct)
    {
        if (sourceId is not > 0) return Json(Array.Empty<object>());
        try
        {
            switch (sourceKind)
            {
                case "alis_irsaliyesi":
                {
                    var lines = await _documents.GetLinesAsync(sourceId.Value, ct);
                    return Json(lines.Where(l => l.ItemId > 0).Select(l => new
                    {
                        itemId = l.ItemId,
                        itemName = l.MaterialName,
                        quantity = l.Quantity,
                        unitId = l.UnitId,
                        unitCode = l.UnitCode,
                    }));
                }
                case "depo_giris":
                {
                    var lines = await _stockDocs.GetLinesAsync(sourceId.Value, ct);
                    return Json(lines.Where(l => l.ItemId > 0).Select(l => new
                    {
                        itemId = l.ItemId,
                        itemName = l.MaterialName,
                        quantity = l.Qty,
                        unitId = l.UnitId,
                        unitCode = l.UnitCode,
                    }));
                }
                case "is_emri":
                {
                    var wo = await _workOrders.GetAsync(sourceId.Value, ct);
                    if (wo is null || wo.ItemId <= 0) return Json(Array.Empty<object>());
                    return Json(new[]
                    {
                        new
                        {
                            itemId = wo.ItemId,
                            itemName = wo.ItemName,
                            quantity = wo.PlannedQuantity,
                            unitId = wo.UnitId,
                            unitCode = wo.UnitCode,
                        },
                    });
                }
                default:
                    return Json(Array.Empty<object>());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kaynak belge kalemleri arama hatası (sourceKind={Kind}, sourceId={Id})", sourceKind, sourceId);
            return Json(Array.Empty<object>());
        }
    }

    // GET /Quality/SourceDocumentLabelLookup?sourceKind=&sourceId=
    // → { label, url } — var olan muayenede kaynak belgeyi salt-okunur göstermek (numara + link).
    [HttpGet]
    public async Task<IActionResult> SourceDocumentLabelLookup(string? sourceKind, int? sourceId, CancellationToken ct)
    {
        if (sourceId is not > 0) return Json(new { label = (string?)null, url = (string?)null });
        try
        {
            switch (sourceKind)
            {
                case "alis_irsaliyesi":
                {
                    var doc = await _documents.GetByIdAsync(sourceId.Value, ct);
                    return Json(new
                    {
                        label = doc?.DocumentNumber,
                        url = doc is null ? null : $"/Sales/DocumentEdit?id={sourceId.Value}",
                    });
                }
                case "depo_giris":
                {
                    var doc = await _stockDocs.GetByIdAsync(sourceId.Value, ct);
                    return Json(new
                    {
                        label = doc?.DocNo,
                        url = doc is null ? null : $"/Warehouse/StockEntryEdit?id={sourceId.Value}",
                    });
                }
                case "is_emri":
                {
                    var wo = await _workOrders.GetAsync(sourceId.Value, ct);
                    return Json(new
                    {
                        label = wo?.OrderNumber,
                        url = wo is null ? null : $"/Production/WorkOrderEdit?id={sourceId.Value}",
                    });
                }
                default:
                    return Json(new { label = (string?)null, url = (string?)null });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kaynak belge etiket arama hatası (sourceKind={Kind}, sourceId={Id})", sourceKind, sourceId);
            return Json(new { label = (string?)null, url = (string?)null });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveInspection([FromBody] SaveQualityInspectionRequest? req, CancellationToken ct)
    {
        if (req is null) return Json(new { ok = false, error = "Geçersiz istek." });
        try { var (ok, error, docId) = await _quality.SaveInspectionAsync(req, CurrentUserId(), ct); return Json(new { ok, error, id = docId }); }
        catch (Exception ex) { _logger.LogError(ex, "Muayene kaydetme hatası (id={Id})", req.Id); return Json(new { ok = false, error = "Kaydedilirken bir hata oluştu." }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeInspectionStatus([FromBody] ChangeInspectionStatusRequest? req, CancellationToken ct)
    {
        if (req is null) return Json(new { ok = false, error = "Geçersiz istek." });
        try { var (ok, error) = await _quality.ChangeInspectionStatusAsync(req, CurrentUserId(), ct); return Json(new { ok, error }); }
        catch (Exception ex) { _logger.LogError(ex, "Muayene durum değişikliği hatası (id={Id})", req.DocumentId); return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDisposition([FromBody] SetInspectionDispositionRequest? req, CancellationToken ct)
    {
        if (req is null) return Json(new { ok = false, error = "Geçersiz istek." });
        try { var (ok, error) = await _quality.SetInspectionDispositionAsync(req, CurrentUserId(), ct); return Json(new { ok, error }); }
        catch (Exception ex) { _logger.LogError(ex, "Uygunsuzluk kararı hatası (id={Id})", req.DocumentId); return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteInspectionJson(int id, CancellationToken ct)
    {
        try { var (ok, error) = await _quality.DeleteInspectionAsync(id, CurrentUserId(), ct); return Json(new { ok, error }); }
        catch (Exception ex) { _logger.LogError(ex, "Muayene silme hatası (id={Id})", id); return Json(new { ok = false, error = "Silinirken bir hata oluştu." }); }
    }

    // GET /Quality/DefectCodesLookup → uygunsuz satır hata kodu dropdown'u
    [HttpGet]
    public async Task<IActionResult> DefectCodesLookup(CancellationToken ct)
        => Json(await _quality.GetDefectCodeOptionsAsync(ct));

    // ════════════════════════════════════════════════════════════════
    // DÖF (Düzeltici/Önleyici Faaliyet — CAPA)
    // ════════════════════════════════════════════════════════════════

    [HttpGet]
    [PermissionScope(FormCodes.QualityCapa)]
    public async Task<IActionResult> Capas(CancellationToken ct)
    {
        ViewBag.BoardConfig = await BuildCapasBoardAsync(ct);
        return View();
    }

    [HttpGet]
    [PermissionScope(FormCodes.QualityCapa)]
    public async Task<IActionResult> CapasBoardConfig(CancellationToken ct)
        => Json(await BuildCapasBoardAsync(ct));

    private async Task<object> BuildCapasBoardAsync(CancellationToken ct)
    {
        var items = await _capa.ListAsync(null, null, ct);
        var now = DateTime.Now;
        var entities = items.Select(m =>
        {
            var badge = (CapaStatus)m.Status switch
            {
                CapaStatus.Acik => (object)new { label = m.StatusLabel, color = "amber" },
                CapaStatus.KokNedenAnaliz => new { label = m.StatusLabel, color = "indigo" },
                CapaStatus.Aksiyonda => new { label = m.StatusLabel, color = "blue" },
                CapaStatus.DogrulamaBekliyor => new { label = m.StatusLabel, color = "violet" },
                CapaStatus.Kapali => new { label = m.StatusLabel, color = "emerald" },
                _ => new { label = m.StatusLabel, color = "slate" },
            };
            var overdue = m.DueDate.HasValue && m.DueDate.Value.Date < now.Date &&
                          (CapaStatus)m.Status is not (CapaStatus.Kapali or CapaStatus.Iptal);
            return new
            {
                id = m.DocumentId,
                title = m.DocumentNumber,
                subtitle = m.Title,
                description = m.ResponsibleName is { Length: > 0 } ? "Sorumlu: " + m.ResponsibleName : null,
                statusBadge = badge,
                widgets = new object[]
                {
                    new { id = "w_type", type = "data", dataType = "options", label = "Tip", value = m.CapaTypeLabel, color = "indigo" },
                    new { id = "w_severity", type = "data", dataType = "options", label = "Önem", value = m.SeverityLabel, color = "amber" },
                    new { id = "w_responsible", type = "data", dataType = "text", label = "Sorumlu", value = m.ResponsibleName ?? "-", color = "slate" },
                    new { id = "w_due", type = "data", dataType = "text", label = "Termin", value = m.DueDate?.ToString("dd.MM.yyyy") ?? "-", color = overdue ? "rose" : "slate" },
                },
                primaryAction = new { label = "Aç", icon = "Edit", color = "amber", url = $"/Quality/CapaEdit?id={m.DocumentId}", hideButton = true },
                secondaryAction = new { label = "Sil", icon = "Trash2", apiUrl = $"/Quality/DeleteCapaJson?id={m.DocumentId}", apiMethod = "POST", confirm = $"Bu DÖF kaydını silmek istediğinize emin misiniz? ({m.DocumentNumber})" },
            };
        }).ToArray();
        return new
        {
            boardKey = "quality-capas",
            title = "Düzeltici/Önleyici Faaliyetler (DÖF)",
            subtitle = $"{items.Count} DÖF",
            icon = "Wrench",
            iconColor = "rose",
            refreshUrl = "/Quality/CapasBoardConfig",
            searchPlaceholder = "Hızlı ara… (belge no, konu)",
            emptyText = "Henüz DÖF kaydı yok. Uygunsuzluk/şikayet kaynaklı düzeltici-önleyici faaliyetleri buradan açarsınız.",
            actions = new object[] { new { id = "new", label = "Yeni DÖF", icon = "Plus", variant = "primary", url = "/Quality/CapaEdit" } },
            masterWidgets = new object[] { },
            entities,
        };
    }

    // sourceKind/sourceId: Muayene (uygunsuzluk) gibi ekranlardan "DÖF Aç" linkiyle yeni kayıt
    // açılırken ön-doldurma. Var olan kayıt (id dolu) bu parametreleri YOK SAYAR — kayıtlı
    // SourceKind/SourceId zaten CapaDetailDto'da gelir.
    [HttpGet]
    [PermissionScope(FormCodes.QualityCapa)]
    public async Task<IActionResult> CapaEdit(int? id, string? sourceKind, int? sourceId, CancellationToken ct)
    {
        CapaDetailDto? model = id is > 0 ? await _capa.GetAsync(id.Value, ct) : null;
        if (model is null)
        {
            // Yeni kayıt — view bu ViewData anahtarlarını okuyup hidden input + preselect için kullanır.
            ViewData["PrefillSourceKind"] = sourceKind;
            ViewData["PrefillSourceId"] = sourceId;
        }
        return View(model);
    }

    // GET /Quality/CapaPersonnelLookup → sorumlu/doğrulayan personel seçici (id, name)
    [HttpGet]
    [PermissionScope(FormCodes.QualityCapa)]
    public async Task<IActionResult> CapaPersonnelLookup(CancellationToken ct)
        => Json(await _capa.GetPersonnelOptionsAsync(ct));

    // GET /Quality/CapaSourceLookup?kind=QualityInspection&search= → [{ sourceKind, sourceId, label, date }]
    // DÖF formunda kaynak kayıt arama/seçici. Bugün tek kind desteklenir: muayene (uygunsuzları öne alır).
    [HttpGet]
    [PermissionScope(FormCodes.QualityCapa)]
    public async Task<IActionResult> CapaSourceLookup(string? kind, string? search, CancellationToken ct)
    {
        try
        {
            switch (kind)
            {
                case "QualityInspection":
                {
                    var inspections = await _quality.ListInspectionsAsync(search, null, null, ct);
                    var ordered = inspections
                        .OrderByDescending(i => i.Verdict == (byte)InspectionVerdict.NonConforming)
                        .ThenByDescending(i => i.Created);
                    return Json(ordered.Select(i => new
                    {
                        sourceKind = "QualityInspection",
                        sourceId = i.DocumentId,
                        label = i.DocumentNumber + (i.VerdictLabel is { Length: > 0 } ? " · " + i.VerdictLabel : ""),
                        date = (DateTime?)i.InspectedAt ?? i.Created,
                    }));
                }
                default:
                    return Json(Array.Empty<object>());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DÖF kaynak kayıt arama hatası (kind={Kind}, search={Search})", kind, search);
            return Json(Array.Empty<object>());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermissionScope(FormCodes.QualityCapa)]
    public async Task<IActionResult> SaveCapa([FromBody] SaveCapaRequest? req, CancellationToken ct)
    {
        if (req is null) return Json(new { ok = false, error = "Geçersiz istek." });
        try
        {
            var (ok, error, docId) = await _capa.SaveAsync(req, CurrentUserId(), ct);
            return Json(new { ok, error, id = docId });
        }
        catch (Exception ex) { _logger.LogError(ex, "DÖF kaydetme hatası (id={Id})", req.Id); return Json(new { ok = false, error = "Kaydedilirken bir hata oluştu." }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermissionScope(FormCodes.QualityCapa)]
    public async Task<IActionResult> ChangeCapaStatus([FromBody] ChangeCapaStatusRequest? req, CancellationToken ct)
    {
        if (req is null) return Json(new { ok = false, error = "Geçersiz istek." });
        try { var (ok, error) = await _capa.ChangeStatusAsync(req, CurrentUserId(), ct); return Json(new { ok, error }); }
        catch (Exception ex) { _logger.LogError(ex, "DÖF durum değişikliği hatası (id={Id})", req.Id); return Json(new { ok = false, error = "İşlem sırasında bir hata oluştu." }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [PermissionScope(FormCodes.QualityCapa)]
    public async Task<IActionResult> DeleteCapaJson(int id, CancellationToken ct)
    {
        try { var (ok, error) = await _capa.DeleteAsync(id, CurrentUserId(), ct); return Json(new { ok, error }); }
        catch (Exception ex) { _logger.LogError(ex, "DÖF silme hatası (id={Id})", id); return Json(new { ok = false, error = "Silinirken bir hata oluştu." }); }
    }
}
