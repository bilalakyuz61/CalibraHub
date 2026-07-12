using CalibraHub.Application.Constants;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Helpers;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(FormCodes.PriceList)]
public sealed class PriceListController : Controller
{
    private readonly IPriceListService _svc;
    private readonly ILogisticsConfigurationService _logistics;
    private readonly ICurrencyService _currencySvc;
    private readonly IFinanceService _finance;
    private readonly ILogger<PriceListController> _logger;

    public PriceListController(
        IPriceListService svc,
        ILogisticsConfigurationService logistics,
        ICurrencyService currencySvc,
        IFinanceService finance,
        ILogger<PriceListController> logger)
    {
        _svc         = svc;
        _logistics   = logistics;
        _currencySvc = currencySvc;
        _finance     = finance;
        _logger      = logger;
    }

    // ── Fiyat Gruplari (CalibraSmartBoard kart sistemi) ─────────────────────
    // Liste = SmartBoard React component (MaterialCards pattern). Her grup bir kart;
    // kart uzerinde Duzenle/Sil/Cariler aksiyonlari ile detay ekrani acilir.

    private const int PriceGroupPageSize = 50;

    [HttpGet]
    public async Task<IActionResult> PriceGroups(CancellationToken ct)
    {
        ViewData["Title"] = "Fiyat Gruplari";
        ViewData["FormCode"] = "PRICE_LIST";
        ViewData["BodyClass"] = "page-price-groups";

        var boardConfig = await BuildPriceGroupsBoardConfigAsync(ct);
        return View(new PriceGroupsViewModel { BoardConfig = boardConfig });
    }

    [HttpGet]
    public IActionResult PriceGroupEdit(int? id)
    {
        // 2026-06-02: Workspace tab iframe HTML'inin browser cache'ine takılıp
        // eski inline JS'in (yanlış formCode vb.) tetiklenmemesi için no-store.
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        ViewData["Title"] = id is > 0 ? "Fiyat Grubu Duzenle" : "Yeni Fiyat Grubu";
        ViewData["FormCode"] = "PRICE_LIST";
        ViewBag.PriceGroupId = id ?? 0;
        return View();
    }

    private async Task<object> BuildPriceGroupsBoardConfigAsync(CancellationToken ct)
    {
        var groups = (await _svc.GetAllGroupsAsync(ct)).ToArray();
        var totalCount = groups.Length;
        var pageItems  = groups.Take(PriceGroupPageSize).ToArray();
        var entities   = BuildPriceGroupEntities(pageItems);

        return new
        {
            boardKey          = "pricelist-price-groups",
            title             = "Fiyat Gruplari",
            subtitle          = totalCount.ToString("N0") + " grup",
            icon              = "Tag",
            iconColor         = "indigo",
            searchPlaceholder = "Grup ara... (kod, ad)",
            emptyText         = "Henuz fiyat grubu tanimlanmamis",
            apiUrl            = "/PriceList/GetPriceGroupsPage",
            totalCount,
            pageSize          = PriceGroupPageSize,
            actions = new[]
            {
                new
                {
                    id      = "new",
                    label   = "Yeni Grup",
                    icon    = "Plus",
                    variant = "primary",
                    url     = "/PriceList/PriceGroupEdit"
                }
            },
            masterWidgets = new List<object>
            {
                SmartBoardFilterHelpers.MakeStdWidget("code",         "Kod",            "text"),
                SmartBoardFilterHelpers.MakeStdWidget("name",         "Ad",             "text"),
                SmartBoardFilterHelpers.MakeStdWidget("isActive",     "Durum",          "boolean"),
                SmartBoardFilterHelpers.MakeStdWidget("allowsTypes",  "Izinli Tipler",  "text"),
            },
            entities
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetPriceGroupsPage(
        int page = 1, int pageSize = 50, string? search = null, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 10 or > 200) pageSize = 50;
        try
        {
            var all = (await _svc.GetAllGroupsAsync(ct)).AsEnumerable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim();
                all = all.Where(g =>
                    g.Code.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    g.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            }
            var arr = all.ToArray();
            var totalCount = arr.Length;
            var pageItems  = arr.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
            var entities   = BuildPriceGroupEntities(pageItems);
            return Json(new { entities, totalCount, page, pageSize });
        }
        catch (Exception ex)
        {
            return Json(new { error = "Islem sirasinda bir hata olustu." });
        }
    }

    private static List<object> BuildPriceGroupEntities(IEnumerable<PriceGroupDto> groups)
    {
        var list = new List<object>();
        foreach (var g in groups)
        {
            // Tipler kisa rozet stringi: "Alis · Satis · Maliyet" gibi
            var allowsList = new List<string>();
            if (g.AllowsBuying)  allowsList.Add("Alis");
            if (g.AllowsSelling) allowsList.Add("Satis");
            if (g.AllowsCost)    allowsList.Add("Maliyet");
            var allowsTxt = allowsList.Count == 0 ? "—" : string.Join(" · ", allowsList);

            // Genel Liste (default) rozeti; degilse Aktif/Pasif.
            object statusBadge = g.IsDefault
                ? new { label = "Genel Liste", color = "violet" }
                : new { label = g.IsActive ? "Aktif" : "Pasif", color = g.IsActive ? "emerald" : "slate" };

            // Genel Liste silinemez → Sil butonu gizlenir (servis de reddeder; ek savunma).
            object? secondaryAction = g.IsDefault
                ? (object?)null
                : new
                {
                    label   = "Sil",
                    icon    = "Trash2",
                    apiUrl  = $"/PriceList/DeletePriceGroupJson?id={g.Id}",
                    confirm = $"'{g.Code}' grubunu silmek istediginize emin misiniz? Tum fiyat kayitlari da silinecek."
                };

            var extraActions = new List<object>
            {
                // Yeni Fiyat Girisi — toplu fiyat giris wizard'i.
                new { id = "new-price", label = "Yeni Fiyat Girisi", icon = "DollarSign", color = "green",
                      url = $"/PriceList/PriceList?groupId={g.Id}" },
                // Raporlama — paginated fiyat tablosu.
                new { id = "report", label = "Raporlama", icon = "Table", color = "amber",
                      url = $"/PriceList/Report?groupId={g.Id}" },
                // Cariler — fiyat grubunu cari kartlara baglar (Contact.PriceGroupId).
                new { id = "contacts", type = "trigger", trigger = "price-group-contacts-modal",
                      label = "Cari Eslestir", icon = "Users", color = "blue",
                      payload = new { groupId = g.Id, groupCode = g.Code ?? string.Empty, groupName = g.Name ?? string.Empty } }
            };
            // Bu grup Genel Liste degilse "Genel Liste Yap" aksiyonu (api-post → onay + in-place refresh).
            if (!g.IsDefault)
                extraActions.Insert(0, new
                {
                    id = "set-default", type = "api-post", label = "Genel Liste Yap", icon = "Star", color = "green",
                    url = $"/PriceList/SetDefaultPriceGroupJson?id={g.Id}",
                    confirm = $"'{g.Name}' grubunu Genel Liste yapmak istediginize emin misiniz? Cari listesi olmayan tum fiyatlar buradan cozulur."
                });

            list.Add(new
            {
                id          = g.Id,
                title       = g.Name ?? "(adsiz)",
                subtitle    = g.Code ?? string.Empty,
                description = g.Description ?? string.Empty,
                imageUrl    = (string?)null,
                statusBadge,
                widgets = new object[]
                {
                    new { id = "code",          type = "data", dataType = "text",    label = "Kod",     value = g.Code },
                    new { id = "name",          type = "data", dataType = "text",    label = "Ad",      value = g.Name },
                    new { id = "isActive",      type = "data", dataType = "boolean", label = "Durum",   value = g.IsActive },
                    new { id = "allowsTypes",   type = "data", dataType = "text",    label = "Izinli Tipler", value = allowsTxt }
                },
                primaryAction = new
                {
                    label = "Duzenle",
                    icon  = "Edit",
                    url   = $"/PriceList/PriceGroupEdit?id={g.Id}"
                },
                secondaryAction,
                extraActions
            });
        }
        return list;
    }

    // ── Fiyat Gruplari JSON Endpoint'leri ───────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAllPriceGroups(string? search, CancellationToken ct)
    {
        var all = await _svc.GetAllGroupsAsync(ct);
        var filtered = string.IsNullOrWhiteSpace(search)
            ? all
            : all.Where(x => x.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
                          || x.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        return Json(filtered.Select(g => new
        {
            g.Id, g.Code, g.Name, g.Description, g.IsActive,
            g.AllowsBuying, g.AllowsSelling, g.AllowsCost, g.IsDefault
        }));
    }

    [HttpGet]
    public async Task<IActionResult> GetPriceGroup(int id, CancellationToken ct)
    {
        var g = await _svc.GetGroupByIdAsync(id, ct);
        if (g is null) return Json(new { success = false, message = "Kayit bulunamadi." });
        return Json(new
        {
            g.Id, g.Code, g.Name, g.Description, g.IsActive,
            g.AllowsBuying, g.AllowsSelling, g.AllowsCost, g.IsDefault
        });
    }

    [HttpPost]
    public async Task<IActionResult> SavePriceGroupJson([FromBody] PriceGroupInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.Name))
            return Json(new { success = false, message = "Kod ve Ad alanlari zorunludur." });

        if (input.Id.HasValue && input.Id.Value > 0)
        {
            var (ok, err) = await _svc.UpdateGroupAsync(
                new UpdatePriceGroupRequest(input.Id.Value, input.Code!, input.Name!, input.Description, input.IsActive,
                    input.AllowsBuying, input.AllowsSelling, input.AllowsCost), ct);
            // Genel Fiyat Listesi isareti (sirket basina tek): true → genel yap, false → kaldir.
            if (ok) await _svc.SetDefaultGroupAsync(input.Id.Value, input.IsDefault, ct);
            return Json(new { success = ok, message = ok ? "Guncellendi." : err, id = input.Id.Value });
        }
        else
        {
            var (ok, err, newId) = await _svc.CreateGroupAsync(
                new CreatePriceGroupRequest(input.Code!, input.Name!, input.Description, input.IsActive,
                    input.AllowsBuying, input.AllowsSelling, input.AllowsCost), ct);
            if (ok && newId.HasValue && input.IsDefault) await _svc.SetDefaultGroupAsync(newId.Value, true, ct);
            return Json(new { success = ok, message = ok ? "Kaydedildi." : err, id = newId });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeletePriceGroupJson(int id, CancellationToken ct)
    {
        var (ok, err) = await _svc.DeleteGroupAsync(id, ct);
        return Json(new { success = ok, message = ok ? "Silindi." : err });
    }

    // Bu grubu "Genel Liste" (default) yap — cari listesi olmayan/urun bulunmayan
    // fiyatlar buradan cozulur. CompanyId basina tek default (servis + index garanti).
    [HttpPost]
    public async Task<IActionResult> SetDefaultPriceGroupJson(int id, CancellationToken ct)
    {
        var (ok, err) = await _svc.SetDefaultGroupAsync(id, true, ct);
        return Json(new { success = ok, message = ok ? "Genel Liste olarak ayarlandi." : err });
    }

    // ── Fiyat Listesi Raporlama (Wizard'a girmeden once filtreli izleme) ────
    // Akis: PriceGroups → secilen grup icin /PriceList/Report?groupId=X (raporlama)
    //      → "Yeni Fiyat Girisi" butonu → /PriceList/PriceList?groupId=X (wizard)
    [HttpGet]
    public IActionResult Report()
    {
        ViewData["Title"] = "Fiyat Listesi — Raporlama";
        ViewData["FormCode"] = "PRICE_LIST";
        return View();
    }

    // ── Fiyat Girisi (Wizard sayfasi) ────────────────────────────────────────

    [HttpGet]
    public IActionResult PriceList()
    {
        ViewData["Title"] = "Fiyat Listesi";
        ViewData["FormCode"] = "PRICE_LIST";
        return View();
    }

    // ── JSON Endpoint'leri ───────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetPriceGroups(CancellationToken ct)
    {
        var groups = await _svc.GetAllGroupsAsync(ct);
        return Json(groups.Where(g => g.IsActive).Select(g => new
        {
            g.Id, g.Code, g.Name, g.Description,
            g.AllowsBuying, g.AllowsSelling, g.AllowsCost
        }));
    }

    [HttpGet]
    public async Task<IActionResult> GetCurrencies(CancellationToken ct)
    {
        var currencies = await _currencySvc.GetAllAsync(ct);
        return Json(currencies.Where(c => c.IsActive).Select(c => new
        {
            c.Id, c.Code, c.Name, c.Symbol
        }));
    }

    [HttpGet]
    public async Task<IActionResult> SearchStocks(string? q, int offset = 0, int pageSize = 50, string? groupCode = null, CancellationToken ct = default)
    {
        if (pageSize <= 0 || pageSize > 200) pageSize = 50;
        if (offset < 0) offset = 0;

        var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        var group  = string.IsNullOrWhiteSpace(groupCode) ? null : groupCode.Trim();
        var (items, totalCount) = await _logistics.GetItemsPagedAsync(search, offset, pageSize, ct, group);

        var results = items.Select(s => new
        {
            id                = s.Id,
            materialCode      = s.Code,
            materialName      = s.Name ?? s.Code,
            trackCombinations = s.Combinations
        }).ToArray();

        return Json(new
        {
            items      = results,
            totalCount = totalCount,
            offset     = offset,
            pageSize   = pageSize,
            hasMore    = offset + results.Length < totalCount
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetMaterialGroups(int? category = null, CancellationToken ct = default)
    {
        // Wizard'in stok seciminde dropdown filtre olarak kullaniliyor.
        // category null ise tum kategorilerden gelir; default kategori 1.
        var groups = await _logistics.GetMaterialGroupsAsync(category, ct);
        return Json(groups
            .OrderBy(g => g.GroupCode)
            .Select(g => new { id = g.Id, code = g.GroupCode, description = g.GroupDescription, category = g.GroupCategory }));
    }

    // ── Cari ↔ Fiyat Grubu eslestirme ───────────────────────────────────────
    // Bir cari sadece BIR fiyat grubuna baglanir (Contact.PriceGroupId NULL veya tek deger).
    // Bir gruba birden fazla cari atanabilir.

    [HttpGet]
    public async Task<IActionResult> GetGroupContacts(int groupId, CancellationToken ct)
    {
        if (groupId <= 0) return Json(Array.Empty<object>());
        var contacts = await _finance.GetContactsByPriceGroupAsync(groupId, ct);
        return Json(contacts.Select(c => new
        {
            id           = c.Id,
            accountCode  = c.AccountCode,
            accountTitle = c.AccountTitle,
            phone        = c.Phone,
            email        = c.Email,
            city         = c.City
        }));
    }

    [HttpGet]
    public async Task<IActionResult> SearchContactsForGroup(string? q, int? excludeGroupId = null, int pageSize = 50, CancellationToken ct = default)
    {
        if (pageSize <= 0 || pageSize > 200) pageSize = 50;
        var (items, _) = await _finance.GetContactsPagedAsync(null, q, 0, pageSize, ct);
        return Json(items.Select(c => new
        {
            id                    = c.Id,
            accountCode           = c.AccountCode,
            accountTitle          = c.AccountTitle,
            currentPriceGroupId   = c.PriceGroupId,
            // Bu grupta zaten varsa frontend "atanmis" diye disable edebilir.
            isAssignedToThisGroup = excludeGroupId.HasValue && c.PriceGroupId == excludeGroupId.Value,
            // Başka bir gruba atanmis mi (uyari icin).
            isAssignedElsewhere   = c.PriceGroupId.HasValue
                                    && (!excludeGroupId.HasValue || c.PriceGroupId.Value != excludeGroupId.Value)
        }));
    }

    // ── Cari kodu manuel yazan kullanıcı için: kodu ID'ye çöz ──
    // 2026-06-02: Rehbere basmadan cari kodu yazıp Ekle'ye basan kullanıcı için.
    [HttpGet]
    public async Task<IActionResult> ResolveContactByCode(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Json(new { ok = false, message = "Cari kodu boş olamaz." });
        var contact = await _finance.GetContactByCodeAsync(code, ct);
        if (contact == null)
            return Json(new { ok = false, message = $"'{code}' kodlu cari bulunamadı." });
        return Json(new
        {
            ok = true,
            data = new
            {
                id           = contact.Id,
                accountCode  = contact.AccountCode,
                accountTitle = contact.AccountTitle,
                phone        = contact.Phone,
                email        = contact.Email,
                city         = contact.City,
            }
        });
    }

    // ── İlk yükleme: SADECE bu gruba atanmış cariler (paginate gerek yok) ──
    // 2026-06-02: GetAllContactsForGroup tüm carileri (344k+) paginate ediyor.
    // Atanmışlar nadiren 100'den fazla — bu endpoint tek seferde döner, frontend
    // varsayılan "Seçilenleri Göster" modunda direkt çağırır. Tümünü Göster
    // basılınca paginate akışına (GetAllContactsForGroup) geçilir.
    [HttpGet]
    public async Task<IActionResult> GetAssignedContactsForGroup(int groupId, CancellationToken ct)
    {
        if (groupId <= 0)
            return Json(new { ok = false, data = Array.Empty<object>(), totalCount = 0, hasMore = false });

        var items = await _finance.GetContactsByPriceGroupAsync(groupId, ct);
        var data = items
            .OrderBy(c => c.AccountCode)
            .Select(c => new
            {
                id                    = c.Id,
                accountCode           = c.AccountCode,
                accountTitle          = c.AccountTitle,
                phone                 = c.Phone,
                email                 = c.Email,
                city                  = c.City,
                currentPriceGroupId   = c.PriceGroupId,
                isAssignedToThisGroup = true,
                isAssignedElsewhere   = false
            })
            .ToList();

        return Json(new
        {
            ok         = true,
            page       = 1,
            pageSize   = data.Count,
            totalCount = data.Count,
            hasMore    = false,
            data
        });
    }

    // ── Toplu Mail Alıcı Listesi pattern: TÜM cariler tek listede, satır başına toggle ───
    // 2026-06-02: Eski iki-aşamalı arama (GetGroupContacts + SearchContactsForGroup) yerine
    // tek endpoint — frontend tüm cariyi yükler, client-side filter + bulk eylem yapar.
    [HttpGet]
    public async Task<IActionResult> GetAllContactsForGroup(
        int groupId, string? q = null, int page = 1, int pageSize = 500, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize <= 0 || pageSize > 2000) pageSize = 500;
        var offset = (page - 1) * pageSize;

        var (items, totalCount) = await _finance.GetContactsPagedAsync(null, q, offset, pageSize, ct);

        // Atanmış olanlar üstte gelsin: önce bu gruba atanmışlar, sonra başka gruba, sonra atanmamış.
        var ordered = items
            .Select(c => new
            {
                id                    = c.Id,
                accountCode           = c.AccountCode,
                accountTitle          = c.AccountTitle,
                phone                 = c.Phone,
                email                 = c.Email,
                city                  = c.City,
                currentPriceGroupId   = c.PriceGroupId,
                isAssignedToThisGroup = c.PriceGroupId.HasValue && c.PriceGroupId.Value == groupId,
                isAssignedElsewhere   = c.PriceGroupId.HasValue && c.PriceGroupId.Value != groupId
            })
            .OrderByDescending(c => c.isAssignedToThisGroup)
            .ThenBy(c => c.accountCode)
            .ToList();

        return Json(new
        {
            ok         = true,
            page,
            pageSize,
            totalCount,
            hasMore    = offset + ordered.Count < totalCount,
            data       = ordered
        });
    }

    public sealed record SetContactPriceGroupRequest(int ContactId, int? PriceGroupId);

    [HttpPost]
    public async Task<IActionResult> SetContactPriceGroup([FromBody] SetContactPriceGroupRequest req, CancellationToken ct)
    {
        if (req is null || req.ContactId <= 0)
            return Json(new { success = false, message = "Geçersiz cari." });
        var (ok, err) = await _finance.SetContactPriceGroupAsync(req.ContactId, req.PriceGroupId, ct);
        return Json(new
        {
            success = ok,
            message = ok
                ? (req.PriceGroupId.HasValue ? "Cari fiyat grubuna atandı." : "Cari fiyat grubundan kaldırıldı.")
                : err
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetCombinations(string materialCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(materialCode))
            return Json(Array.Empty<object>());

        var combos = await _logistics.GetCombinationsForLookupAsync(materialCode.Trim(), ct);
        return Json(combos.Select(c => new
        {
            configId = c.ConfigId,
            code = c.Code,
            name = c.Name,
            featureValues = c.FeatureValues.Select(fv => new { feature = fv.Feature, value = fv.Value, valueCode = fv.ValueCode })
        }));
    }

    [HttpPost]
    public async Task<IActionResult> GetExistingPrices(
        [FromBody] GetExistingPricesRequest request, CancellationToken ct)
    {
        if (request is null || request.GroupId <= 0 || request.Keys == null || request.Keys.Count == 0)
            return Json(Array.Empty<object>());

        var rows = await _svc.GetExistingPricesAsync(request, ct);
        return Json(rows.Select(r => new
        {
            itemId   = r.ItemId,
            configId = r.ConfigId,
            price    = r.Price
        }));
    }

    [HttpGet]
    public async Task<IActionResult> GetPriceListEntries(
        int groupId,
        string? search = null,
        int? currencyId = null,
        string? priceType = null,
        DateTime? validFromMin = null,
        DateTime? validToMax = null,
        DateTime? activeOn = null,
        int page = 1,
        int pageSize = 50,
        int? itemId = null,
        CancellationToken ct = default)
    {
        try
        {
            var filter = new PriceListFilter(
                Search: search,
                CurrencyId: currencyId,
                PriceType: priceType,
                ValidFromMin: validFromMin,
                ValidToMax: validToMax,
                ActiveOn: activeOn,
                Page: page,
                PageSize: pageSize,
                ItemId: itemId);

            var result = await _svc.GetEntriesByGroupAsync(groupId, filter, ct);
            return Json(new
            {
                items = result.Items.Select(e => new
                {
                    e.Id, e.ItemId, e.ItemCode, e.ItemName,
                    e.ConfigId, e.ConfigCode,
                    e.CurrencyId, e.CurrencyCode,
                    e.PriceType, e.Price,
                    validFrom = e.ValidFrom.ToString("yyyy-MM-dd"),
                    validTo   = e.ValidTo?.ToString("yyyy-MM-dd")
                }),
                totalCount = result.TotalCount,
                page = result.Page,
                pageSize = result.PageSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetPriceListEntries hatasi (groupId={GroupId})", groupId);
            return Json(new { success = false, message = "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(
        int groupId,
        string? search = null,
        int? currencyId = null,
        string? priceType = null,
        DateTime? validFromMin = null,
        DateTime? validToMax = null,
        DateTime? activeOn = null,
        CancellationToken ct = default)
    {
        try
        {
            // Tum filtrelenen kayitlari tek seferde cek (cap repo tarafinda 100k).
            var filter = new PriceListFilter(
                Search: search,
                CurrencyId: currencyId,
                PriceType: priceType,
                ValidFromMin: validFromMin,
                ValidToMax: validToMax,
                ActiveOn: activeOn,
                Page: 1,
                PageSize: 100000);

            var result = await _svc.GetEntriesByGroupAsync(groupId, filter, ct);
            var group  = await _svc.GetGroupByIdAsync(groupId, ct);

            using var wb = new XLWorkbook();
            var sheetName = (group?.Code ?? "Fiyat Listesi");
            if (sheetName.Length > 30) sheetName = sheetName.Substring(0, 30);
            var ws = wb.AddWorksheet(sheetName);

            // ── Header ────────────────────────────────────────────────────
            string[] headers = { "Stok Kodu", "Stok Adı", "Kombinasyon", "Döviz", "Tip", "Fiyat", "Başlangıç", "Bitiş" };
            for (var i = 0; i < headers.Length; i++)
            {
                var c = ws.Cell(1, i + 1);
                c.Value = headers[i];
                c.Style.Font.Bold = true;
                c.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
                c.Style.Font.FontColor       = XLColor.White;
                c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // ── Rows ──────────────────────────────────────────────────────
            var row = 2;
            foreach (var e in result.Items)
            {
                ws.Cell(row, 1).Value = e.ItemCode;
                ws.Cell(row, 2).Value = e.ItemName;
                ws.Cell(row, 3).Value = e.ConfigCode ?? "";
                ws.Cell(row, 4).Value = e.CurrencyCode;
                ws.Cell(row, 5).Value = TypeLabel(e.PriceType);

                ws.Cell(row, 6).Value = e.Price;
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";

                ws.Cell(row, 7).Value = e.ValidFrom;
                ws.Cell(row, 7).Style.DateFormat.Format = "dd.MM.yyyy";
                if (e.ValidTo.HasValue)
                {
                    ws.Cell(row, 8).Value = e.ValidTo.Value;
                    ws.Cell(row, 8).Style.DateFormat.Format = "dd.MM.yyyy";
                }
                row++;
            }

            ws.RangeUsed()?.SetAutoFilter();
            ws.Columns().AdjustToContents();
            ws.SheetView.FreezeRows(1);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);

            var fileName = $"FiyatListesi_{group?.Code ?? groupId.ToString()}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(
                ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExportExcel hatasi (groupId={GroupId})", groupId);
            return Content("Excel oluşturulamadı: " + "Islem sirasinda bir hata olustu.", "text/plain");
        }
    }

    private static string TypeLabel(string? t) => (t ?? "").Trim().ToLowerInvariant() switch
    {
        "b" => "Alış",
        "s" => "Satış",
        "m" => "Maliyet",
        _   => t ?? ""
    };

    [HttpPost]
    public async Task<IActionResult> SaveBulkPriceEntries(
        [FromBody] SaveBulkPriceEntriesRequest request, CancellationToken ct)
    {
        try
        {
            var (ok, err, inserted, updated) = await _svc.SaveBulkEntriesAsync(request, ct);
            string? msg = ok
                ? (updated > 0 && inserted > 0
                    ? $"{inserted} yeni kayit eklendi, {updated} kayit guncellendi."
                    : inserted > 0
                        ? $"{inserted} yeni kayit eklendi."
                        : $"{updated} kayit guncellendi.")
                : err;
            return Json(new { success = ok, message = msg, inserted, updated });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Sunucu hatasi: " + "Islem sirasinda bir hata olustu.", inserted = 0, updated = 0 });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdatePriceListJson(
        [FromBody] UpdatePriceEntryRequest request, CancellationToken ct)
    {
        if (request is null || request.Id <= 0)
            return Json(new { success = false, message = "Gecersiz kayit." });
        try
        {
            var (ok, err) = await _svc.UpdateEntryPricesAsync(request, ct);
            return Json(new { success = ok, message = ok ? "Fiyat guncellendi." : err });
        }
        catch (Exception ex)
        {
            // Eski silent-500: persist OLDU ama exception sonrasi UI "sunucu hatasi" diyordu.
            // Simdi exception'i yakaliyor + JSON donuyoruz; kayit yine de DB'de kayitli kalir.
            return Json(new { success = false, message = "Sunucu hatasi: " + "Islem sirasinda bir hata olustu." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeletePriceListJson(int id, CancellationToken ct)
    {
        var (ok, err) = await _svc.DeleteEntryAsync(id, ct);
        return Json(new { success = ok, message = ok ? "Kayit silindi." : err });
    }

}

// ── View Models ───────────────────────────────────────────────────────────────

public sealed class PriceGroupInput
{
    public int? Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // Bu gruba hangi fiyat tipleri girilebilir? Default: hepsi.
    public bool AllowsBuying  { get; set; } = true;
    public bool AllowsSelling { get; set; } = true;
    public bool AllowsCost    { get; set; } = true;
    // Bu liste "Genel Fiyat Listesi" mi? (sirket basina tek). true → digerleri kalkar.
    public bool IsDefault     { get; set; }
}

public sealed class PriceGroupsViewModel
{
    // CalibraSmartBoard inline config — anonymous object, JSON serialize edilecek.
    public object? BoardConfig { get; init; }
}
