using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Helpers;
using CalibraHub.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class GeneralDefinitionsController : Controller
{
    private readonly ISalesRepresentativeService _salesRepService;
    private readonly ICurrencyService _currencyService;
    private readonly ICariGroupService _cariGroupService;

    public GeneralDefinitionsController(
        ISalesRepresentativeService salesRepService,
        ICurrencyService currencyService,
        ICariGroupService cariGroupService)
    {
        _currencyService  = currencyService;
        _salesRepService  = salesRepService;
        _cariGroupService = cariGroupService;
    }

    [HttpGet]
    public async Task<IActionResult> SalesRepresentatives(CancellationToken ct)
    {
        var reps = await _salesRepService.GetAllAsync(ct);
        return View(new SalesRepSmartBoardViewModel { BoardConfig = BuildSalesRepsBoardConfig(reps) });
    }

    [HttpGet("/GeneralDefinitions/SalesReps/BoardEntities")]
    public async Task<IActionResult> SalesRepsBoardEntities(CancellationToken ct)
    {
        var reps = await _salesRepService.GetAllAsync(ct);
        return Json(BuildSalesRepsBoardConfig(reps));
    }

    [HttpGet("/GeneralDefinitions/SalesRepEdit")]
    public async Task<IActionResult> SalesRepEdit(int? id, CancellationToken ct)
    {
        if (!id.HasValue || id.Value <= 0)
            return View(new SalesRepEditViewModel());
        var item = await _salesRepService.GetByIdAsync(id.Value, ct);
        if (item is null) return RedirectToAction(nameof(SalesRepresentatives));
        return View(new SalesRepEditViewModel { Id = item.Id, RepName = item.RepName, IsActive = item.IsActive });
    }

    [HttpPost("/GeneralDefinitions/SalesRepToggle")]
    public async Task<IActionResult> SalesRepToggle([FromQuery] int id, [FromQuery] bool enabled, CancellationToken ct)
    {
        var item = await _salesRepService.GetByIdAsync(id, ct);
        if (item is null) return Json(new { success = false, message = "Bulunamadı" });
        var (ok, err) = await _salesRepService.UpdateAsync(
            new UpdateSalesRepresentativeRequest(id, item.RepName, enabled), ct);
        return Json(new { success = ok, message = err });
    }

    private static object BuildSalesRepsBoardConfig(IReadOnlyCollection<SalesRepresentativeDto> reps)
    {
        var masterWidgets = new List<object>
        {
            SmartBoardFilterHelpers.MakeStdWidget("w_active", "Durum", "boolean"),
        };
        return new {
            boardKey   = "sales-reps",
            title      = "Satış Temsilcileri",
            icon       = "Users",
            iconColor  = "indigo",
            emptyText  = "Henüz satış temsilcisi tanımlanmamış.",
            refreshUrl = "/GeneralDefinitions/SalesReps/BoardEntities",
            actions    = new[] {
                new { id = "new", label = "Yeni Temsilci", icon = "Plus", variant = "primary", url = "/GeneralDefinitions/SalesRepEdit" }
            },
            masterWidgets,
            entities = reps.Select(r => {
                var editUrl = $"/GeneralDefinitions/SalesRepEdit?id={r.Id}";
                return (object)new {
                    id           = r.Id,
                    title        = r.RepName,
                    subtitle     = (string?)null,
                    statusBadge  = new { label = r.IsActive ? "Aktif" : "Pasif", color = r.IsActive ? "emerald" : "slate" },
                    primaryAction = new { type = "navigate", hideButton = true, url = editUrl },
                    widgets      = Array.Empty<object>(),
                    extraActions = new object[] {
                        new { icon = "Edit2",  color = "amber",  tooltip = "Düzenle", type = "navigate", url = editUrl },
                        r.IsActive
                            ? (object)new { icon = "ToggleRight", color = "orange",  tooltip = "Devre Dışı Bırak", type = "api-post", url = $"/GeneralDefinitions/SalesRepToggle?id={r.Id}&enabled=false" }
                            : (object)new { icon = "ToggleLeft",  color = "emerald", tooltip = "Etkinleştir",       type = "api-post", url = $"/GeneralDefinitions/SalesRepToggle?id={r.Id}&enabled=true"  },
                        new { icon = "Trash2", color = "red",    tooltip = "Sil",    type = "api-post", url = $"/GeneralDefinitions/DeleteSalesRepJson?id={r.Id}", confirm = $"\"{r.RepName}\" temsilcisini silmek istediğinizden emin misiniz?" }
                    }
                };
            }).ToArray()
        };
    }

    // ── Satis Temsilcileri JSON API ─────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetSalesReps(string? search, CancellationToken ct)
    {
        var all = await _salesRepService.GetAllAsync(ct);
        if (!string.IsNullOrWhiteSpace(search))
            all = all.Where(x => x.RepName.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        return Json(all);
    }

    [HttpGet]
    public async Task<IActionResult> GetSalesRep(int id, CancellationToken ct)
    {
        var item = await _salesRepService.GetByIdAsync(id, ct);
        return item is null ? NotFound() : Json(item);
    }

    [HttpPost]
    public async Task<IActionResult> SaveSalesRepJson([FromBody] SalesRepresentativeInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.RepName))
            return Json(new { success = false, message = "Ad boş olamaz." });
        if (input.Id.HasValue && input.Id.Value > 0)
        {
            var (ok, err) = await _salesRepService.UpdateAsync(new UpdateSalesRepresentativeRequest(input.Id.Value, input.RepName, input.IsActive), ct);
            return Json(new { success = ok, message = err });
        }
        else
        {
            var (ok, err, newId) = await _salesRepService.CreateAsync(new CreateSalesRepresentativeRequest(input.RepName, input.IsActive), ct);
            return Json(new { success = ok, message = err, id = newId });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteSalesRepJson(int id, CancellationToken ct)
    {
        var (ok, err) = await _salesRepService.DeleteAsync(id, ct);
        return Json(new { success = ok, message = err });
    }

    // ── Cari Gruplari ─────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> CariGroups(CancellationToken ct)
    {
        var groups = await _cariGroupService.GetAllAsync(ct);
        return View(new CariGroupSmartBoardViewModel { BoardConfig = BuildCariGroupsBoardConfig(groups) });
    }

    [HttpGet("/GeneralDefinitions/CariGroups/BoardEntities")]
    public async Task<IActionResult> CariGroupsBoardEntities(CancellationToken ct)
    {
        var groups = await _cariGroupService.GetAllAsync(ct);
        return Json(BuildCariGroupsBoardConfig(groups));
    }

    [HttpGet("/GeneralDefinitions/CariGroupEdit")]
    public async Task<IActionResult> CariGroupEdit(int? id, CancellationToken ct)
    {
        if (!id.HasValue || id.Value <= 0)
            return View(new CariGroupEditViewModel());
        var item = await _cariGroupService.GetByIdAsync(id.Value, ct);
        if (item is null) return RedirectToAction(nameof(CariGroups));
        return View(new CariGroupEditViewModel
        {
            Id = item.Id, Name = item.Name, SortOrder = item.SortOrder, IsActive = item.IsActive,
            GroupCategory = item.GroupCategory
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveCariGroupJson([FromBody] CariGroupInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Json(new { success = false, message = "Grup adi bos olamaz." });
        var category = input.GroupCategory <= 0 ? 1 : (input.GroupCategory > 5 ? 5 : input.GroupCategory);
        if (input.Id.HasValue && input.Id.Value > 0)
        {
            var (ok, err) = await _cariGroupService.UpdateAsync(
                new UpdateCariGroupRequest(input.Id.Value, input.Name, input.SortOrder, input.IsActive, category), ct);
            return Json(new { success = ok, message = err });
        }
        else
        {
            var (ok, err, newId) = await _cariGroupService.CreateAsync(
                new CreateCariGroupRequest(input.Name, input.SortOrder, input.IsActive, category), ct);
            return Json(new { success = ok, message = err, id = newId });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteCariGroupJson(int id, CancellationToken ct)
    {
        var (ok, err) = await _cariGroupService.DeleteAsync(id, ct);
        return Json(new { success = ok, message = err });
    }

    /// <summary>Cari grup dropdown'u icin JSON liste — ContactEdit ve DocLayoutRule/Edit ekranlari kullanir.</summary>
    [HttpGet]
    public async Task<IActionResult> GetCariGroups(CancellationToken ct)
    {
        var all = await _cariGroupService.GetAllAsync(ct);
        return Json(all.Where(g => g.IsActive).Select(g => new { id = g.Id, name = g.Name, code = g.Code }));
    }

    private static object BuildCariGroupsBoardConfig(IReadOnlyCollection<CariGroupDto> groups)
    {
        var categoryOptions = SmartBoardFilterHelpers.ToOptionsList(new[] { "1", "2", "3", "4", "5" });
        var masterWidgets = new List<object>
        {
            SmartBoardFilterHelpers.MakeOptionsWidget("w_category", "Kategori", categoryOptions),
            SmartBoardFilterHelpers.MakeStdWidget("w_sort", "Sira", "numeric"),
        };
        return new {
            boardKey   = "cari-groups",
            title      = "Cari Gruplari",
            icon       = "Users",
            iconColor  = "indigo",
            emptyText  = "Henuz cari grup tanimlanmamis.",
            refreshUrl = "/GeneralDefinitions/CariGroups/BoardEntities",
            actions    = new[] {
                new { id = "new", label = "Yeni Grup", icon = "Plus", variant = "primary", url = "/GeneralDefinitions/CariGroupEdit" }
            },
            masterWidgets,
            entities = groups.Select(g => {
                var editUrl = $"/GeneralDefinitions/CariGroupEdit?id={g.Id}";
                return (object)new {
                    id            = g.Id,
                    title         = g.Name,
                    subtitle      = (string?)null,
                    description   = (string?)null,
                    statusBadge   = new { label = g.IsActive ? "Aktif" : "Pasif", color = g.IsActive ? "emerald" : "slate" },
                    primaryAction = new { type = "navigate", hideButton = true, url = editUrl },
                    widgets       = new object[] {
                        new {
                            id       = "w_category",
                            type     = "data",
                            dataType = "text",
                            label    = "Kategori",
                            value    = g.GroupCategory.ToString(),
                            detail   = (string?)null,
                            color    = "violet",
                        },
                        new {
                            id       = "w_sort",
                            type     = "data",
                            dataType = "numeric",
                            label    = "Sira",
                            value    = g.SortOrder.ToString(),
                            detail   = (string?)null,
                            color    = "indigo",
                        }
                    },
                    extraActions = new object[] {
                        new { icon = "Edit2",  color = "amber", tooltip = "Duzenle", type = "navigate", url = editUrl },
                        new { icon = "Trash2", color = "red",   tooltip = "Sil",    type = "api-post",
                              url = $"/GeneralDefinitions/DeleteCariGroupJson?id={g.Id}",
                              confirm = $"\"{g.Name}\" grubunu silmek istediginizden emin misiniz?" }
                    }
                };
            }).ToArray()
        };
    }

    // ── Doviz Tanimlamalari ──────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Currencies(int? id, string? filterCode, DateTime? fromDate, DateTime? toDate, CancellationToken ct)
    {
        var from = fromDate ?? DateTime.Today;
        var to = toDate ?? DateTime.Today;
        if (from > to) from = to;

        // Kur listesi: doviz kodu filtresi + tarih araligi
        var rateRepo = HttpContext.RequestServices.GetRequiredService<Application.Abstractions.Persistence.IExchangeRateRepository>();
        var allCurrencies = await _currencyService.GetAllAsync(ct);
        var rateList = new List<Application.Contracts.ExchangeRateDto>();

        if (!string.IsNullOrWhiteSpace(filterCode))
        {
            // Tek doviz icin tarih araligindaki kurlar
            var rates = await rateRepo.GetRatesInRangeAsync(filterCode, from, to, ct);
            rateList = rates.Select(r => new Application.Contracts.ExchangeRateDto(r.CurrencyCode, r.Date, r.BuyingRate, r.SellingRate, r.EffectiveBuyingRate, r.EffectiveSellingRate, r.Source)).ToList();
        }
        else
        {
            // Tum dovizler icin son tarihteki kurlar
            var all = await _currencyService.GetAllForDateAsync(to, ct);
            rateList = all.Where(x => x.LatestBuyingRate.HasValue).Select(x => new Application.Contracts.ExchangeRateDto(
                x.Code, x.LatestRateDate ?? to, x.LatestBuyingRate ?? 0, x.LatestSellingRate ?? 0,
                x.EffectiveBuyingRate ?? 0, x.EffectiveSellingRate ?? 0, "")).ToList();
        }

        CurrencyInput input;
        if (id.HasValue)
        {
            var existing = await _currencyService.GetByIdAsync(id.Value, ct);
            if (existing is not null)
            {
                // Secilen tarihteki kur bilgisini bul
                var rateForDate = await rateRepo.GetRateAsync(existing.Code, to, ct);
                input = new CurrencyInput
                {
                    Id = existing.Id, Code = existing.Code, Name = existing.Name,
                    Symbol = existing.Symbol, IsActive = existing.IsActive,
                    BuyingRate = rateForDate?.BuyingRate, SellingRate = rateForDate?.SellingRate,
                    EffectiveBuyingRate = rateForDate?.EffectiveBuyingRate, EffectiveSellingRate = rateForDate?.EffectiveSellingRate
                };
            }
            else input = new CurrencyInput();
        }
        else input = new CurrencyInput();

        return View(new CurrencyViewModel
        {
            Items = allCurrencies,
            RateItems = rateList,
            Input = input,
            FilterCode = filterCode,
            FromDate = from,
            ToDate = to
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCurrency([Bind(Prefix = "Input")] CurrencyInput input, string? search, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(nameof(Currencies), await BuildCurrencyViewModel(input, search, ct));

        int? savedId = input.Id;
        if (input.Id.HasValue && input.Id.Value > 0)
        {
            var (ok, err) = await _currencyService.UpdateAsync(new UpdateCurrencyRequest(input.Id.Value, input.Code, input.Name, input.Symbol, input.IsActive), ct);
            if (!ok) { ModelState.AddModelError("", err ?? "Guncelleme basarisiz."); return View(nameof(Currencies), await BuildCurrencyViewModel(input, search, ct)); }
        }
        else
        {
            var (ok, err, newId) = await _currencyService.CreateAsync(new CreateCurrencyRequest(input.Code, input.Name, input.Symbol, input.IsActive), ct);
            if (!ok) { ModelState.AddModelError("", err ?? "Kayit basarisiz."); return View(nameof(Currencies), await BuildCurrencyViewModel(input, search, ct)); }
            savedId = newId;
        }
        TempData["Success"] = input.Id.HasValue ? "Doviz guncellendi." : "Doviz kaydedildi.";
        return RedirectToAction(nameof(Currencies));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCurrency(int id, string? search, CancellationToken ct)
    {
        var (ok, err) = await _currencyService.DeleteAsync(id, ct);
        if (!ok) TempData["Error"] = err;
        return RedirectToAction(nameof(Currencies));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveManualRate(string currencyCode, DateTime rateDate, decimal buyingRate, decimal sellingRate, decimal effBuyingRate, decimal effSellingRate, string? search, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currencyCode) || buyingRate <= 0 || sellingRate <= 0)
        {
            TempData["Error"] = "Doviz kodu, alis ve satis kuru bos olamaz.";
            return RedirectToAction(nameof(Currencies));
        }
        var rate = new Domain.Entities.ExchangeRate
        {
            CurrencyCode = currencyCode.Trim().ToUpperInvariant(),
            Date = rateDate,
            BuyingRate = buyingRate,
            SellingRate = sellingRate,
            EffectiveBuyingRate = effBuyingRate,
            EffectiveSellingRate = effSellingRate,
            Source = "Manuel"
        };
        var rateRepo = HttpContext.RequestServices.GetRequiredService<Application.Abstractions.Persistence.IExchangeRateRepository>();
        await rateRepo.SaveRatesAsync([rate], ct);
        TempData["Success"] = $"{currencyCode} icin {rateDate:dd.MM.yyyy} tarihli kur kaydedildi.";
        return RedirectToAction(nameof(Currencies));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateExchangeRates(string? search, DateTime? rateDate, CancellationToken ct)
    {
        var date = rateDate ?? DateTime.Today;
        var (ok, err, count) = await _currencyService.UpdateRatesFromTcmbAsync(date, ct);
        TempData[ok ? "Success" : "Error"] = ok ? $"{count} doviz kuru guncellendi ({date:dd.MM.yyyy})." : err;
        return RedirectToAction(nameof(Currencies));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteExchangeRate(string currencyCode, DateTime rateDate, string? search, CancellationToken ct)
    {
        var rateRepo = HttpContext.RequestServices.GetRequiredService<Application.Abstractions.Persistence.IExchangeRateRepository>();
        await rateRepo.DeleteRateAsync(currencyCode, rateDate, ct);
        TempData["Success"] = $"{currencyCode} icin {rateDate:dd.MM.yyyy} tarihli kur silindi.";
        return RedirectToAction(nameof(Currencies));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateExchangeRatesBulk(DateTime fromDate, DateTime toDate, string? search, CancellationToken ct)
    {
        var (ok, err, count) = await _currencyService.UpdateRatesFromTcmbBulkAsync(fromDate, toDate, ct);
        TempData[ok ? "Success" : "Error"] = ok ? $"{count} kur kaydi guncellendi ({fromDate:dd.MM.yyyy} - {toDate:dd.MM.yyyy})." : err;
        return RedirectToAction(nameof(Currencies));
    }

    private async Task<CurrencyViewModel> BuildCurrencyViewModel(CurrencyInput input, string? search, CancellationToken ct)
    {
        var all = await _currencyService.GetAllAsync(ct);
        return new CurrencyViewModel { Items = all, Input = input };
    }

    // ── AJAX JSON Endpoint'leri ─────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetCurrencies(CancellationToken ct)
    {
        var all = await _currencyService.GetAllAsync(ct);
        return Json(all.Where(x => x.Code != "TRY"));
    }

    [HttpGet]
    public async Task<IActionResult> GetCurrency(int id, CancellationToken ct)
    {
        var c = await _currencyService.GetByIdAsync(id, ct);
        return c is null ? NotFound() : Json(c);
    }

    [HttpGet]
    public async Task<IActionResult> GetRates(string? filterCode, DateTime? fromDate, DateTime? toDate, CancellationToken ct)
    {
        var from = fromDate ?? DateTime.Today;
        var to = toDate ?? DateTime.Today;
        var rateRepo = HttpContext.RequestServices.GetRequiredService<Application.Abstractions.Persistence.IExchangeRateRepository>();

        // Karsilastirma mantigi icin aralik geriye dogru 30 gun genisletilir — hafta sonu / 15:30
        // oncesi klonlarini atlayip gercekten farkli bir degere sahip onceki kuru bulabilmek icin.
        var historyFrom = from.AddDays(-30);

        IReadOnlyCollection<Domain.Entities.ExchangeRate> extendedRates;
        if (!string.IsNullOrWhiteSpace(filterCode))
            extendedRates = await rateRepo.GetRatesInRangeAsync(filterCode, historyFrom, to, ct);
        else
            extendedRates = await rateRepo.GetAllRatesInRangeAsync(historyFrom, to, ct);

        var allCurrencies = await _currencyService.GetAllAsync(ct);
        var nameMap = allCurrencies.ToDictionary(c => c.Code, c => c.Name, StringComparer.OrdinalIgnoreCase);

        // Yalnizca tanimlanmis dovizlerin kurlari — TCMB'den gelmis olsa bile tanimsiz kodlar gizlenir.
        var known = extendedRates.Where(r => nameMap.ContainsKey(r.CurrencyCode)).ToList();

        // Para birimi bazli ASC siralanmis tarihce. Klon satirlari (ayni degeri tasiyan
        // ardisik kayitlar) atlanarak gercek anlamda farkli olan onceki kur bulunur.
        var history = known
            .GroupBy(r => r.CurrencyCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Date).ToList(), StringComparer.OrdinalIgnoreCase);

        var prevMap = new Dictionary<Domain.Entities.ExchangeRate, Domain.Entities.ExchangeRate?>();
        foreach (var kv in history)
        {
            var asc = kv.Value;
            for (var i = 0; i < asc.Count; i++)
            {
                Domain.Entities.ExchangeRate? prev = null;
                for (var j = i - 1; j >= 0; j--)
                {
                    // Ayni degeri tasiyan klon satirlari atla (hafta sonu Cuma kuru, 15:30
                    // oncesi dunku kur). Gercekten degismis bir degere rastlayinca dur.
                    if (asc[j].BuyingRate > 0 && asc[j].BuyingRate != asc[i].BuyingRate)
                    {
                        prev = asc[j];
                        break;
                    }
                }
                prevMap[asc[i]] = prev;
            }
        }

        // Kullaniciya sadece istenen [from, to] araliginda olan kurlari goster — tarihce kayitlari
        // yalnizca karsilastirma icin kullanildi.
        var displayed = known
            .Where(r => r.Date.Date >= from.Date && r.Date.Date <= to.Date)
            .ToList();

        return Json(displayed.Select(r =>
        {
            var prev = prevMap.TryGetValue(r, out var p) ? p : null;
            return new {
                r.CurrencyCode,
                currencyName = nameMap.TryGetValue(r.CurrencyCode, out var n) ? n : "",
                rateDate = r.Date.ToString("yyyy-MM-dd"),
                rateDateDisplay = r.Date.ToString("dd.MM.yyyy"),
                r.BuyingRate, r.SellingRate, r.EffectiveBuyingRate, r.EffectiveSellingRate,
                prevBuyingRate = prev?.BuyingRate,
                prevSellingRate = prev?.SellingRate,
                prevEffectiveBuyingRate = prev?.EffectiveBuyingRate,
                prevEffectiveSellingRate = prev?.EffectiveSellingRate,
                prevRateDateDisplay = prev?.Date.ToString("dd.MM.yyyy")
            };
        }));
    }

    [HttpGet]
    public async Task<IActionResult> GetCurrencyRate(int id, DateTime? rateDate, CancellationToken ct)
    {
        var c = await _currencyService.GetByIdAsync(id, ct);
        if (c is null) return NotFound();
        var date = rateDate ?? DateTime.Today;
        var rateRepo = HttpContext.RequestServices.GetRequiredService<Application.Abstractions.Persistence.IExchangeRateRepository>();
        var rate = await rateRepo.GetRateAsync(c.Code, date, ct);
        return Json(new { c.Id, c.Code, c.Name, c.Symbol, c.IsActive, buyingRate = rate?.BuyingRate, sellingRate = rate?.SellingRate, effectiveBuyingRate = rate?.EffectiveBuyingRate, effectiveSellingRate = rate?.EffectiveSellingRate });
    }

    [HttpPost]
    public async Task<IActionResult> SaveCurrencyJson([FromBody] CurrencyInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.Name))
            return Json(new { success = false, message = "Kod ve ad bos olamaz." });

        if (input.Id.HasValue && input.Id.Value > 0)
        {
            var (ok, err) = await _currencyService.UpdateAsync(new UpdateCurrencyRequest(input.Id.Value, input.Code, input.Name, input.Symbol, input.IsActive), ct);
            return Json(new { success = ok, message = err });
        }
        else
        {
            var (ok, err, newId) = await _currencyService.CreateAsync(new CreateCurrencyRequest(input.Code, input.Name, input.Symbol, input.IsActive), ct);
            return Json(new { success = ok, message = err, id = newId });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteCurrencyJson(int id, CancellationToken ct)
    {
        var (ok, err) = await _currencyService.DeleteAsync(id, ct);
        return Json(new { success = ok, message = err });
    }

    [HttpPost]
    public async Task<IActionResult> SaveRateJson([FromBody] SaveRateRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.CurrencyCode)) return Json(new { success = false, message = "Doviz kodu bos." });
        var rate = new Domain.Entities.ExchangeRate {
            CurrencyCode = req.CurrencyCode.Trim().ToUpperInvariant(), Date = req.RateDate,
            BuyingRate = req.BuyingRate, SellingRate = req.SellingRate,
            EffectiveBuyingRate = req.EffectiveBuyingRate, EffectiveSellingRate = req.EffectiveSellingRate,
            Source = "Manuel"
        };
        var rateRepo = HttpContext.RequestServices.GetRequiredService<Application.Abstractions.Persistence.IExchangeRateRepository>();
        await rateRepo.SaveRatesAsync([rate], ct);
        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteRateJson(string currencyCode, DateTime rateDate, CancellationToken ct)
    {
        var rateRepo = HttpContext.RequestServices.GetRequiredService<Application.Abstractions.Persistence.IExchangeRateRepository>();
        await rateRepo.DeleteRateAsync(currencyCode, rateDate, ct);
        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> FetchTcmbRatesJson(DateTime? fromDate, DateTime? toDate, CancellationToken ct)
    {
        var from = fromDate ?? DateTime.Today;
        var to = toDate ?? DateTime.Today;
        if (from == to)
        {
            var (ok, err, count) = await _currencyService.UpdateRatesFromTcmbAsync(from, ct);
            return Json(new { success = ok, message = ok ? $"{count} kur guncellendi." : err });
        }
        else
        {
            var (ok, err, count) = await _currencyService.UpdateRatesFromTcmbBulkAsync(from, to, ct);
            return Json(new { success = ok, message = ok ? $"{count} kur guncellendi." : err });
        }
    }

    public sealed record SaveRateRequest(string CurrencyCode, DateTime RateDate, decimal BuyingRate, decimal SellingRate, decimal EffectiveBuyingRate, decimal EffectiveSellingRate);
}
