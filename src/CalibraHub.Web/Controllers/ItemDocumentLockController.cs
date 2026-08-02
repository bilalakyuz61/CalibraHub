using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Helpers;
using CalibraHub.Web.Models.Logistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// Malzeme Belge Kilitleri — merkezi yönetim ekranı (Faz 1+2, 2026-07-25, PageComment Seq 30).
///
/// Mevcut kilit mekanizması (ItemDocumentLock tablosu, tekil kaydet/oku) MaterialController'da
/// zaten vardı (malzeme kartı sekmesinden düzenleme); bu controller AYRI/bağımsız yetki
/// (FormCodes.ItemDocumentLock) altında TÜM malzemeleri tek ekrandan gösterip toplu işlem
/// yapan tamamlayıcı bir yönetim panelidir. MaterialController'a dokunulmadı (kasıtlı — ayrı
/// yetki kapsamı korunuyor).
///
/// KRİTİK bağımlılık — bu görev tamamlandığında hâlâ eksik: <c>FormCodes.ItemDocumentLock</c>
/// (ITEM_DOCUMENT_LOCK) dbo.Forms tablosuna SeedFormsAsync (CalibraDatabaseInitializer.cs,
/// db uzmanı sahası) ile seed edilmeden bu ekran SADECE SystemAdmin/DepartmentManager (admin)
/// için çalışır — PermissionDefDiscoveryService yalnız dbo.Forms'taki satırlar için CRUD
/// PermissionDef üretir, dolayısıyla normal (grantable) kullanıcılar Yetki Yönetimi
/// matrisinde bu formu hiç göremez/yetkilendirilemez. Bkz. rapor.
/// </summary>
[Authorize]
[Route("Logistics/[action]")]
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.ItemDocumentLock)]
public sealed class ItemDocumentLockController : Controller
{
    private const int DefaultPageSize = 50;
    private const string BoardKey = "logistics-item-document-locks";

    private readonly ILogisticsConfigurationService _service;
    private readonly ILogger<ItemDocumentLockController> _logger;

    public ItemDocumentLockController(
        ILogisticsConfigurationService service,
        ILogger<ItemDocumentLockController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // GET /Logistics/ItemDocumentLocks — ekran (combined payload: config + ilk sayfa).
    [HttpGet]
    public async Task<IActionResult> ItemDocumentLocks(CancellationToken ct)
    {
        ViewData["Title"] = "Malzeme Belge Kilitleri";
        ViewData["FormCode"] = FormCodes.ItemDocumentLock;
        var boardConfig = await BuildBoardConfigAsync(null, null, ct);
        // View dosyasi Views/Logistics/ altinda (route Logistics/[action]); controller adi
        // ItemDocumentLock oldugundan konvansiyonel arama Views/ItemDocumentLock/'a bakar ve
        // bulamaz → explicit yol ver.
        return View("~/Views/Logistics/ItemDocumentLocks.cshtml", new ItemDocumentLocksViewModel { BoardConfig = boardConfig });
    }

    // GET /Logistics/ItemDocumentLocksBoardConfig?lockedDocType=&search=
    // Hızlı filtre çipi değişimi (mountHandle.refresh) + refreshUrl (kayıt sonrası in-place
    // yenileme) İKİSİ için de kullanılır — SmartBoard refreshBoard() yalnız data.entities okur,
    // fazladan alanlar (lockCounts, docTypeCatalog vb.) yok sayılır, zararsız.
    [HttpGet]
    public async Task<IActionResult> ItemDocumentLocksBoardConfig(string? lockedDocType, string? search, CancellationToken ct)
    {
        try
        {
            var config = await BuildBoardConfigAsync(lockedDocType, search, ct);
            return Json(config);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ItemDocumentLock] BoardConfig okunamadı (lockedDocType={LockedDocType})", lockedDocType);
            return Json(new { error = "İşlem sırasında bir hata oluştu." });
        }
    }

    // GET /Logistics/ItemDocumentLocksPage/{lockedDocType?}?page=&pageSize=&search=
    // NOT: apiUrl SmartBoard.jsx tarafından KOŞULSUZ '?page=X&pageSize=Y[&search=Z]' ile
    // string-concat edilir (bkz. fetchPage) — aktif filtre bu yüzden QUERY STRING değil
    // ROUTE SEGMENT ile taşınır (aksi halde ikinci '?' geçersiz URL üretir).
    [HttpGet("/Logistics/ItemDocumentLocksPage/{lockedDocType?}")]
    public async Task<IActionResult> ItemDocumentLocksPage(
        string? lockedDocType, int page = 1, int pageSize = DefaultPageSize, string? search = null, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 10) pageSize = 10;
        if (pageSize > 200) pageSize = 200;
        var offset = (page - 1) * pageSize;

        try
        {
            var (items, totalCount) = await _service.GetItemsPagedByDocumentLockAsync(lockedDocType, search, offset, pageSize, ct);
            var entities = await BuildEntitiesAsync(items, ct);
            return Json(new { entities, totalCount, page, pageSize });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ItemDocumentLock] Sayfa okunamadı (lockedDocType={LockedDocType}, page={Page})", lockedDocType, page);
            return Json(new { error = "İşlem sırasında bir hata oluştu." });
        }
    }

    // GET /Logistics/ItemDocumentLockGet?itemId=123 — "Kilitleri Düzenle" panelini açarken
    // güncel kilit setini taze okur (board'daki recordValues önizleme amaçlıdır, kaydetme
    // öncesi burası otorite kaynağıdır).
    [HttpGet]
    public async Task<IActionResult> ItemDocumentLockGet(int itemId, CancellationToken ct)
    {
        if (itemId <= 0) return Json(new { error = "Malzeme seçimi gerekli." });
        try
        {
            var docTypes = await _service.GetItemDocumentLocksAsync(itemId, ct);
            return Json(new { itemId, docTypes });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ItemDocumentLock] Kilit okunamadı (itemId={ItemId})", itemId);
            return Json(new { error = "İşlem sırasında bir hata oluştu." });
        }
    }

    // POST /Logistics/ItemDocumentLockSave — tekil malzeme icin tam kilit setini kaydeder
    // (mevcut LogisticsConfigurationService.SaveItemDocumentLocksAsync sarmalanır — audit
    // servis katmanında, bkz. rapor).
    [HttpPost]
    public async Task<IActionResult> ItemDocumentLockSave([FromBody] ItemDocumentLockSaveInput input, CancellationToken ct)
    {
        if (input is null || input.ItemId <= 0)
            return Json(new { success = false, message = "Malzeme seçimi gerekli." });

        try
        {
            await _service.SaveItemDocumentLocksAsync(input.ItemId, input.DocTypes ?? [], ct);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ItemDocumentLock] Kilit kaydedilemedi (itemId={ItemId})", input.ItemId);
            return Json(new { success = false, message = "Kilitler kaydedilemedi." });
        }
    }

    // POST /Logistics/ItemDocumentLockBulkSave — { itemIds[], docTypes[], mode:'lock'|'unlock' }
    [HttpPost]
    public async Task<IActionResult> ItemDocumentLockBulkSave([FromBody] ItemDocumentLockBulkSaveInput input, CancellationToken ct)
    {
        if (input is null || input.ItemIds is not { Length: > 0 })
            return Json(new { success = false, message = "En az bir malzeme seçilmeli." });
        if (input.DocTypes is not { Length: > 0 })
            return Json(new { success = false, message = "En az bir belge tipi seçilmeli." });

        var isLock = !string.Equals(input.Mode, "unlock", StringComparison.OrdinalIgnoreCase);

        try
        {
            var result = await _service.BulkSetItemDocumentLocksAsync(input.ItemIds, input.DocTypes, isLock, ct);
            return Json(new
            {
                success = true,
                requestedItemCount = result.RequestedItemCount,
                updatedItemCount = result.UpdatedItemCount,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ItemDocumentLock] Toplu kilit işlemi başarısız (mode={Mode}, itemCount={ItemCount})",
                input.Mode, input.ItemIds.Length);
            return Json(new { success = false, message = "Toplu işlem gerçekleştirilemedi." });
        }
    }

    // GET /Logistics/ItemDocumentLockSearchItems?search=&page=&pageSize= — "Toplu İşlem"
    // panelindeki malzeme seçici için hafif arama ucu (MaterialController/LogisticsController'daki
    // Kod/Ad araması ile AYNI servis metodunu kullanır — ITEM_DOCUMENT_LOCK yetkisiyle bağımsız
    // erişilebilir olsun diye MaterialCardEdit-scoped uçlara bağımlı kalınmadı).
    [HttpGet]
    public async Task<IActionResult> ItemDocumentLockSearchItems(string? search, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 5) pageSize = 5;
        if (pageSize > 100) pageSize = 100;
        var offset = (page - 1) * pageSize;

        try
        {
            var (items, totalCount) = await _service.GetItemsPagedAsync(search, offset, pageSize, ct);
            var result = items.Select(x => new { id = x.Id, code = x.Code, name = x.Name }).ToList();
            return Json(new { items = result, totalCount, page, pageSize });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ItemDocumentLock] Malzeme arama başarısız (search={Search})", search);
            return Json(new { error = "İşlem sırasında bir hata oluştu." });
        }
    }

    // ── Board config + entity builder ────────────────────────────────

    private async Task<object> BuildBoardConfigAsync(string? lockedDocType, string? search, CancellationToken ct)
    {
        var normalizedType = string.IsNullOrWhiteSpace(lockedDocType) ? null : lockedDocType.Trim();
        var isValidType = normalizedType != null && ItemDocumentLockTypes.IsValidCode(normalizedType);
        var effectiveType = isValidType ? normalizedType : null;

        var (items, totalCount) = await _service.GetItemsPagedByDocumentLockAsync(effectiveType, search, 0, DefaultPageSize, ct);
        var entities = await BuildEntitiesAsync(items, ct);
        var lockCounts = await _service.GetItemDocumentLockCountsByDocTypeAsync(ct);

        var docTypeCatalog = ItemDocumentLockTypes.All
            .Select(t => (object)new
            {
                code = t.Code,
                label = t.Label,
                count = lockCounts.TryGetValue(t.Code, out var c) ? c : 0,
            })
            .ToList();

        var activeLabel = effectiveType != null ? ItemDocumentLockTypes.LabelFor(effectiveType) : null;
        var pageUrl = effectiveType != null
            ? $"/Logistics/ItemDocumentLocksPage/{Uri.EscapeDataString(effectiveType)}"
            : "/Logistics/ItemDocumentLocksPage";
        var refreshUrl = effectiveType != null
            ? $"/Logistics/ItemDocumentLocksBoardConfig?lockedDocType={Uri.EscapeDataString(effectiveType)}"
            : "/Logistics/ItemDocumentLocksBoardConfig";

        return new
        {
            boardKey = BoardKey,
            title = activeLabel != null ? $"Malzeme Belge Kilitleri — {activeLabel}" : "Malzeme Belge Kilitleri",
            subtitle = activeLabel != null
                ? $"{totalCount:N0} malzeme — {activeLabel} kilitli"
                : $"{totalCount:N0} malzeme",
            icon = "Lock",
            iconColor = "rose",
            searchPlaceholder = "Malzeme ara... (kod, ad)",
            emptyText = activeLabel != null ? "Bu belge tipinde kilitli malzeme yok" : "Henüz malzeme tanımlanmamış",
            apiUrl = pageUrl,
            refreshUrl,
            totalCount,
            pageSize = DefaultPageSize,
            skipInitialFetch = true,
            // Ozel alanlar — SmartBoard.jsx bunları yok sayar, sayfadaki kendi çip/toplu-işlem
            // JS'imiz okur (bkz. Views/Logistics/ItemDocumentLocks.cshtml).
            activeLockedDocType = effectiveType,
            docTypeCatalog,
            // Toplu İşlem board header aksiyonu — trigger ile sayfadaki window.idlOpenBulk'u
            // çağırır (bkz. Views/Logistics/ItemDocumentLocks.cshtml). Belge-tipi hızlı filtre
            // çipleri kaldırıldı; filtreleme artık filtre panelinden (w_locked_types) yapılır.
            actions = new object[]
            {
                new { id = "bulk", label = "Toplu İşlem", icon = "Layers", variant = "primary", trigger = "idlOpenBulk" },
            },
            masterWidgets = new List<object>
            {
                SmartBoardFilterHelpers.MakeStdWidget("w_grup", "Grup", "text"),
                SmartBoardFilterHelpers.MakeOptionsWidget("w_locked_types", "Kilitli Tipler",
                    SmartBoardFilterHelpers.ToOptionsList(ItemDocumentLockTypes.All.Select(t => (t.Code, t.Label)))),
            },
            entities,
        };
    }

    private async Task<List<object>> BuildEntitiesAsync(IReadOnlyCollection<ItemDto> items, CancellationToken ct)
    {
        var itemIds = items.Select(x => x.Id).ToArray();
        var lockMap = await _service.GetAllItemDocumentLocksGroupedAsync(ct);
        var groupMappings = itemIds.Length > 0
            ? await _service.GetMaterialGroupMappingsBatchAsync(itemIds, ct)
            : new Dictionary<int, IReadOnlyList<MaterialGroupMappingDto>>();

        var entities = new List<object>();
        foreach (var item in items)
        {
            var lockedTypes = lockMap.TryGetValue(item.Id, out var lt) ? lt : Array.Empty<string>();
            var lockedCount = lockedTypes.Count;
            var lockedLabels = lockedTypes.Select(ItemDocumentLockTypes.LabelFor)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

            groupMappings.TryGetValue(item.Id, out var itemGroups);
            var primaryGroup = itemGroups?.FirstOrDefault(g => g.SlotOrder == 1);
            var groupDisplay = primaryGroup == null
                ? null
                : (string.IsNullOrWhiteSpace(primaryGroup.GroupDescription) ? primaryGroup.GroupCode : primaryGroup.GroupDescription);

            var widgets = new List<object>();
            if (!string.IsNullOrWhiteSpace(groupDisplay))
                widgets.Add(new { id = "w_grup", type = "data", dataType = "text", label = "Grup", value = groupDisplay, color = "violet" });
            widgets.Add(new
            {
                id = "w_locked_types",
                type = "data",
                dataType = "options",
                label = "Kilitli Tipler",
                value = lockedTypes.Count == 0 ? null : string.Join(",", lockedTypes),
                detail = lockedLabels.Count == 0 ? null : string.Join(", ", lockedLabels),
                color = lockedCount > 0 ? "rose" : "slate",
            });

            entities.Add(new
            {
                id = item.Id,
                title = item.Name,
                subtitle = item.Code,
                description = (string?)null,
                imageUrl = (string?)null,
                statusBadge = lockedCount > 0
                    ? new { label = $"{lockedCount} Kilit", color = "rose" }
                    : new { label = "Kilit Yok", color = "slate" },
                widgets,
                recordValues = new Dictionary<string, object?>
                {
                    ["id"] = item.Id,
                    ["code"] = item.Code,
                    ["name"] = item.Name,
                    ["lockedDocTypes"] = lockedTypes,
                },
                primaryAction = new
                {
                    label = "Kilitleri Düzenle",
                    icon = "Lock",
                    url = $"#idl-{item.Id}",
                },
            });
        }
        return entities;
    }
}

/// <summary>Tekil malzeme belge kilidi kaydetme girdisi.</summary>
public sealed record ItemDocumentLockSaveInput(int ItemId, string[]? DocTypes);

/// <summary>Toplu malzeme belge kilidi kaydetme girdisi — Mode: "lock" (varsayılan) | "unlock".</summary>
public sealed record ItemDocumentLockBulkSaveInput(int[]? ItemIds, string[]? DocTypes, string? Mode);
