using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Web.Controllers;

[Authorize]
[Route("[controller]")]
public sealed class DocLayoutRuleController : Controller
{
    private readonly IDocLayoutRuleService _svc;
    private readonly IDocDesignerService _layoutSvc;
    private readonly IFinanceService _financeSvc;
    private readonly IFinanceRepository _financeRepo;
    private readonly IUserProfileRepository _userRepo;
    private readonly ILogisticsConfigurationService _logisticsSvc;
    private readonly ILogger<DocLayoutRuleController> _logger;

    public DocLayoutRuleController(
        IDocLayoutRuleService svc,
        IDocDesignerService layoutSvc,
        IFinanceService financeSvc,
        IFinanceRepository financeRepo,
        IUserProfileRepository userRepo,
        ILogisticsConfigurationService logisticsSvc,
        ILogger<DocLayoutRuleController> logger)
    {
        _svc = svc;
        _layoutSvc = layoutSvc;
        _financeSvc = financeSvc;
        _financeRepo = financeRepo;
        _userRepo = userRepo;
        _logisticsSvc = logisticsSvc;
        _logger = logger;
    }

    // ── Sözlükler ────────────────────────────────────────────────────────────

    // 2026-05-23: Tum satis + satin alma + depo belgeleri eklendi.
    // Yeni belge tipi eklenince DocTypeLabels ve DocTypeColors'a ayni anahtarla satir ekleyin.
    private static readonly Dictionary<string, string> DocTypeLabels = new()
    {
        // ── Satis ──
        ["sales_quote"]      = "Satış Teklifi",
        ["sales_order"]      = "Satış Siparişi",
        // ── Satin Alma ──
        ["purchase_request"] = "İhtiyaç Kaydı",
        ["purchase_quote"]   = "Satın Alma Teklif",
        ["purchase_order"]   = "Satın Alma Sipariş",
        // ── Depo ──
        ["transfer"]         = "Depo Transferi",
        ["stock_in"]         = "Ambar Giriş",
        ["stock_out"]        = "Ambar Çıkış",
        ["inventory_count"]  = "Sayım",
        // ── Fatura / Belge ──
        ["delivery_note"]    = "İrsaliye",
        ["invoice"]          = "Fatura",
        ["expense_note"]     = "Gider Pusulası",
        ["custom"]           = "Özel Belge",
    };

    private static readonly Dictionary<string, string> DocTypeColors = new()
    {
        // ── Satis (indigo/blue ailesi) ──
        ["sales_quote"]      = "indigo",
        ["sales_order"]      = "blue",
        // ── Satin Alma (amber/violet ailesi) ──
        ["purchase_request"] = "amber",
        ["purchase_quote"]   = "blue",
        ["purchase_order"]   = "violet",
        // ── Depo (emerald ailesi) ──
        ["transfer"]         = "emerald",
        ["stock_in"]         = "emerald",
        ["stock_out"]        = "rose",
        ["inventory_count"]  = "amber",
        // ── Fatura / Belge ──
        ["delivery_note"]    = "emerald",
        ["invoice"]          = "amber",
        ["expense_note"]     = "rose",
        ["custom"]           = "slate",
    };

    // ── Sayfalar ─────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        return View(config);
    }

    [HttpGet("BoardConfig")]
    public async Task<IActionResult> BoardConfig(CancellationToken ct)
    {
        var config = await BuildBoardConfigAsync(ct);
        return Json(config);
    }

    [HttpGet("New")]
    public async Task<IActionResult> New(CancellationToken ct)
    {
        await PopulateLookupsAsync(null, ct);
        return View("Edit", new DocLayoutRuleEditVm());
    }

    [HttpGet("Edit/{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var rule = await _svc.GetAsync(id, ct);
        if (rule is null) return NotFound();

        await PopulateLookupsAsync(rule.CustomerId, ct);

        var vm = new DocLayoutRuleEditVm
        {
            Id             = rule.Id,
            DocType        = rule.DocType,
            LayoutId       = rule.LayoutId,
            CustomerId     = rule.CustomerId,
            ContactGroupId = rule.ContactGroupId,
            UserId         = rule.UserId?.ToString(),
            BranchId       = rule.BranchId,
            WarehouseId    = rule.WarehouseId,
            IsActive       = rule.IsActive,
        };
        return View("Edit", vm);
    }

    private async Task PopulateLookupsAsync(int? customerIdForPreload, CancellationToken ct)
    {
        ViewBag.DocTypes = DocTypeLabels;
        ViewBag.Layouts  = await _layoutSvc.ListAsync(null, ct);

        // Users: tüm aktif kullanıcılar (dropdown — küçük dataset)
        var users = await _userRepo.GetAllAsync(ct);
        ViewBag.Users = users
            .OrderBy(u => u.FullName)
            .Select(u => new { id = u.Id, name = u.FullName, email = u.Email })
            .ToList();

        // Locations (Depo): tümünü çek; LocationName + Code göster
        var locations = await _logisticsSvc.GetLocationsAsync(ct);
        ViewBag.Locations = locations
            .OrderBy(l => l.LocationName ?? l.LocationCode)
            .Select(l => new { id = l.Id, name = (l.LocationName ?? l.LocationCode) + " (" + l.LocationCode + ")" })
            .ToList();

        // Customer initial display: edit moddaysa seçili olanın adını yükle
        // (single-row lookup — 5000+ kayitlik tabloyu tarayan GetContactsAsync degil)
        ViewBag.SelectedCustomerName = null;
        if (customerIdForPreload.HasValue)
        {
            var match = await _financeSvc.GetContactByIdAsync(customerIdForPreload.Value, ct);
            if (match != null) ViewBag.SelectedCustomerName = match.AccountTitle;
        }
    }

    // ── Lookup endpoint'leri ─────────────────────────────────────────────────

    /// <summary>Cari için autocomplete kaynağı (isim/kod araması).
    /// q boş veya 2 karakterden kısaysa boş dizi döner — kullanıcı yazana kadar
    /// 5000+ kayitlik tablo bos yere taranmaz.</summary>
    [HttpGet("SearchContactsJson")]
    public async Task<IActionResult> SearchContactsJson([FromQuery] string? q, CancellationToken ct)
    {
        var query = (q ?? string.Empty).Trim();
        if (query.Length < 2) return Json(Array.Empty<object>());

        // Repository LIKE araması ile filtreliyor; sadece ilk 20 satıra ihtiyaç var.
        // (GetContactsPagedAsync TOP olmadan tüm satırları COUNT() OVER() ile dönerken
        //  burada en hızlı yol: paged + offset=0, pageSize=20.)
        var paged = await _financeRepo.GetContactsPagedAsync(null, query, 0, 20, ct);
        var rows = paged.Items
            .Select(c => new
            {
                id    = c.Id,
                label = c.AccountTitle,
                code  = c.AccountCode,
            })
            .ToList();
        return Json(rows);
    }

    // ── API ──────────────────────────────────────────────────────────────────

    [HttpPost("SaveJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveJson([FromForm] DocLayoutRuleEditVm input, CancellationToken ct)
    {
        _logger.LogInformation(
            "[DLR.SaveJson] IN id={Id} docType={DocType} layoutId={LayoutId} isActive={IsActive} custIds={CustIds} userIds={UserIds} legacyCust={LegacyCust} legacyUser={LegacyUser}",
            input?.Id, input?.DocType, input?.LayoutId, input?.IsActive, input?.CustomerIds, input?.UserIds, input?.CustomerId, input?.UserId);

        if (input is null) return Json(new { ok = false, error = "Geçersiz istek." });
        if (string.IsNullOrWhiteSpace(input.DocType))
            return Json(new { ok = false, error = "Belge tipi seçilmelidir." });
        if (input.LayoutId <= 0)
            return Json(new { ok = false, error = "Bir tasarım seçilmelidir." });

        // Çoklu seçim parsing — UI CSV gönderir: "1,3,7" / "guid1,guid2"
        // Boş veya tek değer durumunda da legacy single-value alanlar (CustomerId/UserId)
        // kullanılabilir; iki yöntem birleştirilir (UI yeni alanları doldurursa
        // legacy de senkronize gönderilir).
        var customerIds = ParseIntList(input.CustomerIds);
        if (customerIds.Count == 0 && input.CustomerId.HasValue)
            customerIds.Add(input.CustomerId.Value);

        var userIds = ParseUserIntList(input.UserIds, out var userParseError);
        if (userParseError != null) return Json(new { ok = false, error = userParseError });
        if (userIds.Count == 0 && !string.IsNullOrWhiteSpace(input.UserId))
        {
            if (!int.TryParse(input.UserId, out var parsed))
                return Json(new { ok = false, error = "Geçersiz kullanıcı ID formatı." });
            userIds.Add(parsed);
        }

        // Hiç seçim yoksa wildcard mode: tek kural, null değerlerle.
        // Aksi halde cartesian product: customers × users → N rule.
        var custList = customerIds.Count > 0 ? customerIds.Select(c => (int?)c).ToList() : new List<int?> { null };
        var userList = userIds.Count     > 0 ? userIds.Select(g => (int?)g).ToList()    : new List<int?> { null };

        try
        {
            var savedIds = new List<int>();
            bool firstIteration = true;
            foreach (var cid in custList)
            {
                foreach (var uid in userList)
                {
                    // Düzenleme modu (input.Id > 0) sadece İLK kombinasyona uygulanır;
                    // ek kombinasyonlar yeni kural olarak eklenir. Aksi halde aynı kural
                    // sürekli üzerine yazılır ve N kural oluşmaz.
                    var saveId = firstIteration ? input.Id : 0;
                    var id = await _svc.SaveAsync(new SaveDocLayoutRuleRequest(
                        Id:             saveId,
                        DocType:        input.DocType,
                        LayoutId:       input.LayoutId,
                        CustomerId:     cid,
                        UserId:         uid,
                        BranchId:       input.BranchId,
                        WarehouseId:    input.WarehouseId,
                        IsActive:       input.IsActive,
                        ContactGroupId: input.ContactGroupId
                    ), ct);
                    savedIds.Add(id);
                    firstIteration = false;
                }
            }
            _logger.LogInformation("[DLR.SaveJson] OK savedIds=[{Ids}] count={Count}",
                string.Join(",", savedIds), savedIds.Count);
            return Json(new { ok = true, id = savedIds.FirstOrDefault(), ids = savedIds, count = savedIds.Count });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[DLR.SaveJson] InvalidOperation: {Msg}", ex.Message);
            return Json(new { ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DLR.SaveJson] Unexpected: {Msg}", ex.Message);
            return Json(new { ok = false, error = "Kayıt sırasında beklenmeyen hata: " + ex.Message });
        }
    }

    // CSV "1,3,7" → List<int>; boş/whitespace string'ler atılır.
    private static List<int> ParseIntList(string? csv)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(csv)) return result;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var v)) result.Add(v);
        }
        return result.Distinct().ToList();
    }

    // CSV "1,2,3" → List<int>; geçersiz formatta error mesajı döner.
    private static List<int> ParseUserIntList(string? csv, out string? error)
    {
        error = null;
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(csv)) return result;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var n))
            {
                error = $"Geçersiz kullanıcı ID formatı: {part}";
                return new List<int>();
            }
            result.Add(n);
        }
        return result.Distinct().ToList();
    }

    [HttpPost("DeleteJson")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteJson(int id, CancellationToken ct)
    {
        try
        {
            await _svc.DeleteAsync(id, ct);
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    /// <summary>
    /// DocType'a göre kullanılabilir tasarımları döner (cascading dropdown için).
    /// </summary>
    [HttpGet("LayoutsByDocType")]
    public async Task<IActionResult> LayoutsByDocType([FromQuery] string docType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(docType))
            return Json(Array.Empty<object>());

        var layouts = await _layoutSvc.ListAsync(docType, ct);
        return Json(layouts.Select(l => new { id = l.Id, name = l.Name, code = l.Code }));
    }

    // ── Board Config ─────────────────────────────────────────────────────────

    private async Task<object> BuildBoardConfigAsync(CancellationToken ct)
    {
        var rules   = await _svc.ListAllAsync(ct);
        var layouts = await _layoutSvc.ListAsync(null, ct);
        var layoutById = layouts.ToDictionary(l => l.Id);
        _logger.LogInformation("[DLR.BuildBoardConfig] rules.Count={RulesCount} layouts.Count={LayoutsCount}",
            rules.Count, layouts.Count);
        var docTypeOptions = SmartBoardFilterHelpers.ToOptionsList(DocTypeLabels.Values.Distinct());
        var masterWidgets = new List<object>
        {
            SmartBoardFilterHelpers.MakeOptionsWidget("w_doctype",  "Belge Tipi", docTypeOptions),
            SmartBoardFilterHelpers.MakeStdWidget   ("w_layout",   "Tasarım",    "text"),
            SmartBoardFilterHelpers.MakeStdWidget   ("w_priority", "Öncelik",    "numeric"),
        };

        var entities = rules.Select(r =>
        {
            var docLabel = DocTypeLabels.TryGetValue(r.DocType, out var lbl) ? lbl : r.DocType;
            var docColor = DocTypeColors.TryGetValue(r.DocType, out var col) ? col : "slate";

            // Kriter özet metni
            var criteriaParts = new List<string>();
            if (r.CustomerId.HasValue)  criteriaParts.Add($"Cari #{r.CustomerId.Value}");
            if (r.UserId.HasValue)      criteriaParts.Add("Kullanıcı bağımlı");
            if (r.BranchId.HasValue)    criteriaParts.Add($"Şube #{r.BranchId.Value}");
            if (r.WarehouseId.HasValue) criteriaParts.Add($"Depo #{r.WarehouseId.Value}");
            var criteriaText = criteriaParts.Count == 0 ? "Genel (wildcard)" : string.Join(" · ", criteriaParts);

            return (object)new
            {
                id          = r.Id,
                title       = r.LayoutName,
                subtitle    = (string?)null,
                description = criteriaText,
                imageUrl    = (string?)null,
                statusBadge = r.Weight == 0
                    ? new { label = "Wildcard", color = "slate" }
                    : new { label = $"Ağırlık {r.Weight}", color = "emerald" },
                widgets = new object[]
                {
                    new
                    {
                        id       = "w_doctype",
                        type     = "data",
                        dataType = "options",
                        label    = "Belge Tipi",
                        value    = docLabel,
                        detail   = (string?)null,
                        color    = docColor,
                    },
                    new
                    {
                        id       = "w_layout",
                        type     = "data",
                        dataType = "text",
                        label    = "Tasarım",
                        value    = r.LayoutName,
                        detail   = (string?)null,
                        color    = "indigo",
                    },
                    new
                    {
                        id       = "w_priority",
                        type     = "data",
                        dataType = "numeric",
                        label    = "Öncelik",
                        value    = r.Weight.ToString(),
                        detail   = "ağırlık",
                        color    = r.Weight >= 12 ? "emerald" : r.Weight >= 4 ? "amber" : "slate",
                    },
                },
                primaryAction = new
                {
                    label      = "Düzenle",
                    icon       = "Edit",
                    color      = "amber",
                    url        = $"/DocLayoutRule/Edit/{r.Id}",
                    hideButton = false,
                },
                secondaryAction = new
                {
                    label     = "Sil",
                    icon      = "Trash2",
                    apiUrl    = $"/DocLayoutRule/DeleteJson?id={r.Id}",
                    apiMethod = "POST",
                    confirm   = $"Bu kuralı silmek istediğinize emin misiniz? ({docLabel} → {r.LayoutName})",
                },
            };
        }).ToList();

        return new
        {
            boardKey          = "doc-layout-rules",
            title             = "Belge Tasarım Kuralları",
            subtitle          = $"{entities.Count} kural",
            icon              = "GitBranch",
            iconColor         = "indigo",
            refreshUrl        = "/DocLayoutRule/BoardConfig",
            searchPlaceholder = "Tasarım adı veya kriter…",
            emptyText         = "Henüz kural yok — \"Yeni Kural\" ile ekleyin",
            actions = new object[]
            {
                new { id = "new", label = "Yeni Kural", icon = "Plus", variant = "primary", url = "/DocLayoutRule/New" },
            },
            masterWidgets,
            entities,
        };
    }

    /// <summary>Edit form için view model. UserId form'dan string gelir;
    /// SaveJson içinde Guid'e parse edilir (boş string Guid? binding'ini bozuyor).</summary>
    public sealed class DocLayoutRuleEditVm
    {
        public int      Id             { get; set; }
        public string?  DocType        { get; set; }
        public int      LayoutId       { get; set; }
        public int?     CustomerId     { get; set; }   // legacy/backward-compat — tek değer
        public int?     ContactGroupId { get; set; }
        public string?  UserId         { get; set; }   // legacy/backward-compat — tek değer
        public int?     BranchId       { get; set; }
        public int?     WarehouseId    { get; set; }
        public bool     IsActive       { get; set; } = true;

        // Çoklu seçim alanları — CSV ("1,3,5" veya "guid1,guid2"). UI yeni form'dan gelir.
        // Boş veya null ise legacy CustomerId/UserId kullanılır.
        public string?  CustomerIds    { get; set; }
        public string?  UserIds        { get; set; }
    }
}
