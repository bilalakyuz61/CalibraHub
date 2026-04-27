using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class GeneralDefinitionsController : Controller
{
    private readonly ISalesRepresentativeService _salesRepService;
    private readonly ICurrencyService _currencyService;

    public GeneralDefinitionsController(ISalesRepresentativeService salesRepService, ICurrencyService currencyService)
    {
        _currencyService = currencyService;
        _salesRepService = salesRepService;
    }

    [HttpGet]
    public async Task<IActionResult> SalesRepresentatives(int? id, string? search, CancellationToken ct)
    {
        var all = await _salesRepService.GetAllAsync(ct);
        var filtered = all.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(x =>
                x.RepCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.RepName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        SalesRepresentativeInput input;
        if (id.HasValue)
        {
            var existing = await _salesRepService.GetByIdAsync(id.Value, ct);
            input = existing is not null
                ? new SalesRepresentativeInput { Id = existing.Id, RepCode = existing.RepCode, RepName = existing.RepName, IsActive = existing.IsActive }
                : new SalesRepresentativeInput();
        }
        else
        {
            input = new SalesRepresentativeInput();
        }

        return View(new SalesRepresentativeViewModel
        {
            Items = filtered.ToArray(),
            Input = input,
            Search = search,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSalesRepresentative(
        [Bind(Prefix = "Input")] SalesRepresentativeInput input,
        string? search, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(nameof(SalesRepresentatives), await BuildViewModel(input, search, ct));

        int? savedId = input.Id;

        if (input.Id.HasValue && input.Id.Value > 0)
        {
            var (ok, err) = await _salesRepService.UpdateAsync(
                new UpdateSalesRepresentativeRequest(input.Id.Value, input.RepCode, input.RepName, input.IsActive), ct);
            if (!ok) { ModelState.AddModelError("", err ?? "Guncelleme basarisiz."); return View(nameof(SalesRepresentatives), await BuildViewModel(input, search, ct)); }
        }
        else
        {
            var (ok, err, newId) = await _salesRepService.CreateAsync(
                new CreateSalesRepresentativeRequest(input.RepCode, input.RepName, input.IsActive), ct);
            if (!ok) { ModelState.AddModelError("", err ?? "Kayit basarisiz."); return View(nameof(SalesRepresentatives), await BuildViewModel(input, search, ct)); }
            savedId = newId;
        }

        return RedirectToAction(nameof(SalesRepresentatives), new { id = savedId, search });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSalesRepresentative(int id, string? search, CancellationToken ct)
    {
        await _salesRepService.DeleteAsync(id, ct);
        return RedirectToAction(nameof(SalesRepresentatives), new { search });
    }

    private async Task<SalesRepresentativeViewModel> BuildViewModel(SalesRepresentativeInput input, string? search, CancellationToken ct)
    {
        var all = await _salesRepService.GetAllAsync(ct);
        var filtered = all.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
            filtered = filtered.Where(x => x.RepCode.Contains(search, StringComparison.OrdinalIgnoreCase) || x.RepName.Contains(search, StringComparison.OrdinalIgnoreCase));
        return new SalesRepresentativeViewModel { Items = filtered.ToArray(), Input = input, Search = search };
    }

    // ── Satis Temsilcileri JSON API ─────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetSalesReps(string? search, CancellationToken ct)
    {
        var all = await _salesRepService.GetAllAsync(ct);
        if (!string.IsNullOrWhiteSpace(search))
            all = all.Where(x => x.RepCode.Contains(search, StringComparison.OrdinalIgnoreCase) || x.RepName.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
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
        if (string.IsNullOrWhiteSpace(input.RepCode) || string.IsNullOrWhiteSpace(input.RepName))
            return Json(new { success = false, message = "Kod ve ad bos olamaz." });
        if (input.Id.HasValue && input.Id.Value > 0)
        {
            var (ok, err) = await _salesRepService.UpdateAsync(new UpdateSalesRepresentativeRequest(input.Id.Value, input.RepCode, input.RepName, input.IsActive), ct);
            return Json(new { success = ok, message = err });
        }
        else
        {
            var (ok, err, newId) = await _salesRepService.CreateAsync(new CreateSalesRepresentativeRequest(input.RepCode, input.RepName, input.IsActive), ct);
            return Json(new { success = ok, message = err, id = newId });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteSalesRepJson(int id, CancellationToken ct)
    {
        var (ok, err) = await _salesRepService.DeleteAsync(id, ct);
        return Json(new { success = ok, message = err });
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
            rateList = rates.Select(r => new Application.Contracts.ExchangeRateDto(r.CurrencyCode, r.RateDate, r.BuyingRate, r.SellingRate, r.EffectiveBuyingRate, r.EffectiveSellingRate, r.Source)).ToList();
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
            RateDate = rateDate,
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
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.RateDate).ToList(), StringComparer.OrdinalIgnoreCase);

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
            .Where(r => r.RateDate.Date >= from.Date && r.RateDate.Date <= to.Date)
            .ToList();

        return Json(displayed.Select(r =>
        {
            var prev = prevMap.TryGetValue(r, out var p) ? p : null;
            return new {
                r.CurrencyCode,
                currencyName = nameMap.TryGetValue(r.CurrencyCode, out var n) ? n : "",
                rateDate = r.RateDate.ToString("yyyy-MM-dd"),
                rateDateDisplay = r.RateDate.ToString("dd.MM.yyyy"),
                r.BuyingRate, r.SellingRate, r.EffectiveBuyingRate, r.EffectiveSellingRate,
                prevBuyingRate = prev?.BuyingRate,
                prevSellingRate = prev?.SellingRate,
                prevEffectiveBuyingRate = prev?.EffectiveBuyingRate,
                prevEffectiveSellingRate = prev?.EffectiveSellingRate,
                prevRateDateDisplay = prev?.RateDate.ToString("dd.MM.yyyy")
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
            CurrencyCode = req.CurrencyCode.Trim().ToUpperInvariant(), RateDate = req.RateDate,
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
