using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class PriceListController : Controller
{
    private readonly IPriceListService _svc;
    private readonly ILogisticsConfigurationService _logistics;
    private readonly ICurrencyService _currencySvc;

    public PriceListController(
        IPriceListService svc,
        ILogisticsConfigurationService logistics,
        ICurrencyService currencySvc)
    {
        _svc         = svc;
        _logistics   = logistics;
        _currencySvc = currencySvc;
    }

    // ── Fiyat Gruplari (mevcut — form-post) ──────────────────────────────────

    [HttpGet]
    public IActionResult PriceGroups()
    {
        ViewData["Title"] = "Fiyat Listesi";
        ViewData["FormCode"] = "PRICE_LIST";
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePriceGroup(
        [Bind(Prefix = "Input")] PriceGroupInput input, string? search, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(nameof(PriceGroups), await BuildGroupVm(input, search, ct));

        int? savedId;
        if (input.Id.HasValue && input.Id.Value > 0)
        {
            var (ok, err) = await _svc.UpdateGroupAsync(
                new UpdatePriceGroupRequest(input.Id.Value, input.GroupCode!, input.GroupName!, input.Description, input.IsActive), ct);
            if (!ok) { ModelState.AddModelError("", err ?? "Guncelleme basarisiz."); return View(nameof(PriceGroups), await BuildGroupVm(input, search, ct)); }
            savedId = input.Id.Value;
        }
        else
        {
            var (ok, err, newId) = await _svc.CreateGroupAsync(
                new CreatePriceGroupRequest(input.GroupCode!, input.GroupName!, input.Description, input.IsActive), ct);
            if (!ok) { ModelState.AddModelError("", err ?? "Kayit basarisiz."); return View(nameof(PriceGroups), await BuildGroupVm(input, search, ct)); }
            savedId = newId;
        }

        return RedirectToAction(nameof(PriceGroups), new { id = savedId, search });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePriceGroup(int id, string? search, CancellationToken ct)
    {
        await _svc.DeleteGroupAsync(id, ct);
        return RedirectToAction(nameof(PriceGroups), new { search });
    }

    // ── Fiyat Gruplari JSON Endpoint'leri ───────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAllPriceGroups(string? search, CancellationToken ct)
    {
        var all = await _svc.GetAllGroupsAsync(ct);
        var filtered = string.IsNullOrWhiteSpace(search)
            ? all
            : all.Where(x => x.GroupCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                          || x.GroupName.Contains(search, StringComparison.OrdinalIgnoreCase));
        return Json(filtered.Select(g => new
        {
            g.Id, g.GroupCode, g.GroupName, g.Description, g.IsActive
        }));
    }

    [HttpGet]
    public async Task<IActionResult> GetPriceGroup(int id, CancellationToken ct)
    {
        var g = await _svc.GetGroupByIdAsync(id, ct);
        if (g is null) return Json(new { success = false, message = "Kayit bulunamadi." });
        return Json(new { g.Id, g.GroupCode, g.GroupName, g.Description, g.IsActive });
    }

    [HttpPost]
    public async Task<IActionResult> SavePriceGroupJson([FromBody] PriceGroupInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.GroupCode) || string.IsNullOrWhiteSpace(input.GroupName))
            return Json(new { success = false, message = "Kod ve Ad alanlari zorunludur." });

        if (input.Id.HasValue && input.Id.Value > 0)
        {
            var (ok, err) = await _svc.UpdateGroupAsync(
                new UpdatePriceGroupRequest(input.Id.Value, input.GroupCode!, input.GroupName!, input.Description, input.IsActive), ct);
            return Json(new { success = ok, message = ok ? "Guncellendi." : err, id = input.Id.Value });
        }
        else
        {
            var (ok, err, newId) = await _svc.CreateGroupAsync(
                new CreatePriceGroupRequest(input.GroupCode!, input.GroupName!, input.Description, input.IsActive), ct);
            return Json(new { success = ok, message = ok ? "Kaydedildi." : err, id = newId });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeletePriceGroupJson(int id, CancellationToken ct)
    {
        var (ok, err) = await _svc.DeleteGroupAsync(id, ct);
        return Json(new { success = ok, message = ok ? "Silindi." : err });
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
            g.Id, g.GroupCode, g.GroupName, g.Description
        }));
    }

    [HttpGet]
    public async Task<IActionResult> GetCurrencies(CancellationToken ct)
    {
        var currencies = await _currencySvc.GetAllAsync(ct);
        return Json(currencies.Where(c => c.IsActive).Select(c => new
        {
            c.Code, c.Name, c.Symbol
        }));
    }

    [HttpGet]
    public async Task<IActionResult> SearchStocks(string? q, CancellationToken ct)
    {
        var cards = await _logistics.GetStockCardsForLookupAsync(ct);
        var query = (q ?? "").Trim().ToLowerInvariant();
        var results = cards
            .Where(s => string.IsNullOrEmpty(query)
                     || s.MaterialCode.ToLowerInvariant().Contains(query)
                     || (s.MaterialName?.ToLowerInvariant().Contains(query) ?? false))
            .Take(200)
            .Select(s => new { id = s.Id, materialCode = s.MaterialCode, materialName = s.MaterialName ?? s.MaterialCode });
        return Json(results);
    }

    [HttpGet]
    public async Task<IActionResult> GetPriceListEntries(int groupId, CancellationToken ct)
    {
        var entries = await _svc.GetEntriesByGroupAsync(groupId, ct);
        return Json(entries.Select(e => new
        {
            e.Id, e.MaterialCode, e.MaterialName,
            e.Currency, e.BuyingPrice, e.SellingPrice,
            validFrom = e.ValidFrom.ToString("yyyy-MM-dd"),
            validTo = e.ValidTo?.ToString("yyyy-MM-dd")
        }));
    }

    [HttpPost]
    public async Task<IActionResult> SaveBulkPriceEntries(
        [FromBody] SaveBulkPriceEntriesRequest request, CancellationToken ct)
    {
        try
        {
            var (ok, err, count) = await _svc.SaveBulkEntriesAsync(request, ct);
            return Json(new { success = ok, message = ok ? $"{count} kayit basariyla eklendi." : err, count });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Sunucu hatasi: " + ex.Message, count = 0 });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdatePriceListEntryJson(
        [FromBody] UpdatePriceEntryRequest request, CancellationToken ct)
    {
        if (request.Id <= 0)
            return Json(new { success = false, message = "Gecersiz kayit." });
        var (ok, err) = await _svc.UpdateEntryPricesAsync(request, ct);
        return Json(new { success = ok, message = ok ? "Fiyat guncellendi." : err });
    }

    [HttpPost]
    public async Task<IActionResult> DeletePriceListEntryJson(int id, CancellationToken ct)
    {
        var (ok, err) = await _svc.DeleteEntryAsync(id, ct);
        return Json(new { success = ok, message = ok ? "Kayit silindi." : err });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<PriceGroupsViewModel> BuildGroupVm(PriceGroupInput input, string? search, CancellationToken ct)
    {
        var all = await _svc.GetAllGroupsAsync(ct);
        var filtered = string.IsNullOrWhiteSpace(search)
            ? all
            : all.Where(x => x.GroupCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                          || x.GroupName.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        return new PriceGroupsViewModel { Items = filtered.ToArray(), Input = input, Search = search };
    }
}

// ── View Models ───────────────────────────────────────────────────────────────

public sealed class PriceGroupInput
{
    public int? Id { get; set; }
    public string? GroupCode { get; set; }
    public string? GroupName { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PriceGroupsViewModel
{
    public IReadOnlyCollection<PriceGroupDto> Items { get; set; } = [];
    public PriceGroupInput Input { get; set; } = new();
    public string? Search { get; set; }
}
